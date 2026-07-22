"""Crop a DICOM image series to the slice extent of an RTSTRUCT ROI."""

from __future__ import annotations

import copy
import os
import shutil
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import pydicom
from pydicom.dataset import Dataset
from pydicom.sequence import Sequence
from pydicom.uid import generate_uid

from artemis_preprocessing.dicom.copy_structures import copy_structures
from artemis_preprocessing.utils import get_datetime


CROPPABLE_MR_SERIES_DESCRIPTION_PREFIX = "sCT_sp_Pel_T2"


@dataclass(frozen=True)
class CropResult:
    """Outcome of an attempted registered-series crop."""

    status: str
    retained_count: int = 0
    deleted_count: int = 0
    roi_name: str | None = None
    original_rows: int | None = None
    original_columns: int | None = None
    cropped_rows: int | None = None
    cropped_columns: int | None = None
    source_series_uid: str | None = None
    derived_series_uid: str | None = None
    warning: str | None = None
    warning_code: str | None = None
    caudal_missing_mm: float = 0.0
    cranial_missing_mm: float = 0.0
    error: str | None = None

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass(frozen=True)
class _Slice:
    path: Path
    position: float
    sop_instance_uid: str
    sop_class_uid: str
    image_position: np.ndarray
    row_direction: np.ndarray
    column_direction: np.ndarray
    pixel_spacing: np.ndarray
    rows: int
    columns: int


class CropSeriesError(RuntimeError):
    """Raised internally when a crop cannot be completed safely."""


class IneligibleCropSeriesError(CropSeriesError):
    """Raised when a series is intentionally excluded from image cropping."""


def _as_vector(value, *, length: int, field: str) -> np.ndarray:
    if value is None or len(value) != length:
        raise CropSeriesError(f"Missing or invalid {field}")
    try:
        result = np.asarray([float(item) for item in value], dtype=float)
    except (TypeError, ValueError) as exc:
        raise CropSeriesError(f"Invalid numeric values in {field}") from exc
    if not np.all(np.isfinite(result)):
        raise CropSeriesError(f"Non-finite values in {field}")
    return result


def _load_series_slices(directory: str, series_uid: str) -> tuple[list[_Slice], np.ndarray]:
    records: list[tuple[Path, Dataset]] = []
    for path in Path(directory).rglob("*.dcm"):
        try:
            ds = pydicom.dcmread(str(path), stop_before_pixels=True, force=True)
        except Exception:
            continue
        if str(getattr(ds, "SeriesInstanceUID", "")) != str(series_uid):
            continue
        if getattr(ds, "Modality", "") in {"RTSTRUCT", "REG"}:
            continue
        records.append((path, ds))

    if not records:
        raise CropSeriesError(f"No image slices found for series {series_uid}")

    if any(
        str(getattr(ds, "Modality", "") or "").strip() != "MR"
        or not str(
            getattr(ds, "SeriesDescription", "") or ""
        ).strip().startswith(CROPPABLE_MR_SERIES_DESCRIPTION_PREFIX)
        for _, ds in records
    ):
        raise IneligibleCropSeriesError(
            "Only MR series with SeriesDescription starting with "
            f"'{CROPPABLE_MR_SERIES_DESCRIPTION_PREFIX}' are eligible for "
            "cropping; leaving the image series unchanged"
        )

    first_iop = _as_vector(
        getattr(records[0][1], "ImageOrientationPatient", None),
        length=6,
        field="ImageOrientationPatient",
    )
    normal = np.cross(first_iop[:3], first_iop[3:])
    norm = float(np.linalg.norm(normal))
    if norm < 1e-6:
        raise CropSeriesError("Image orientation produces an invalid slice normal")
    normal /= norm
    first_pixel_spacing = _as_vector(
        getattr(records[0][1], "PixelSpacing", None),
        length=2,
        field="PixelSpacing",
    )
    if np.any(first_pixel_spacing <= 0):
        raise CropSeriesError("PixelSpacing must contain positive values")
    try:
        first_rows = int(getattr(records[0][1], "Rows", 0))
        first_columns = int(getattr(records[0][1], "Columns", 0))
    except (TypeError, ValueError) as exc:
        raise CropSeriesError("Invalid Rows or Columns") from exc

    slices: list[_Slice] = []
    seen_sops: set[str] = set()
    for path, ds in records:
        iop = _as_vector(
            getattr(ds, "ImageOrientationPatient", None),
            length=6,
            field=f"ImageOrientationPatient in {path.name}",
        )
        slice_normal = np.cross(iop[:3], iop[3:])
        slice_norm = float(np.linalg.norm(slice_normal))
        if slice_norm < 1e-6:
            raise CropSeriesError(f"Invalid image orientation in {path.name}")
        slice_normal /= slice_norm
        if abs(float(np.dot(normal, slice_normal))) < 1.0 - 1e-4:
            raise CropSeriesError("Image slices do not share a consistent orientation")
        if not np.allclose(iop, first_iop, rtol=0, atol=1e-5):
            raise CropSeriesError("Image slices do not share identical orientation")

        ipp = _as_vector(
            getattr(ds, "ImagePositionPatient", None),
            length=3,
            field=f"ImagePositionPatient in {path.name}",
        )
        pixel_spacing = _as_vector(
            getattr(ds, "PixelSpacing", None),
            length=2,
            field=f"PixelSpacing in {path.name}",
        )
        if not np.allclose(pixel_spacing, first_pixel_spacing, rtol=0, atol=1e-5):
            raise CropSeriesError("Image slices do not share identical pixel spacing")
        try:
            rows = int(getattr(ds, "Rows", 0))
            columns = int(getattr(ds, "Columns", 0))
        except (TypeError, ValueError) as exc:
            raise CropSeriesError(f"Invalid Rows or Columns in {path.name}") from exc
        if rows != first_rows or columns != first_columns:
            raise CropSeriesError("Image slices do not share identical dimensions")
        if getattr(ds, "Modality", "") not in {"CT", "MR"}:
            raise CropSeriesError(f"Unsupported modality in {path.name}")
        if int(getattr(ds, "NumberOfFrames", 1) or 1) != 1:
            raise CropSeriesError("Multiframe images are not supported")
        if int(getattr(ds, "SamplesPerPixel", 0) or 0) != 1:
            raise CropSeriesError("Only single-sample monochrome images are supported")
        if getattr(ds, "PhotometricInterpretation", "") not in {
            "MONOCHROME1",
            "MONOCHROME2",
        }:
            raise CropSeriesError("Only monochrome images are supported")
        transfer_syntax = getattr(ds.file_meta, "TransferSyntaxUID", None)
        if transfer_syntax is None:
            raise CropSeriesError(f"Missing Transfer Syntax UID in {path.name}")
        if transfer_syntax.is_compressed:
            raise CropSeriesError("Compressed image data is not supported")
        sop_uid = str(getattr(ds, "SOPInstanceUID", "") or "")
        sop_class_uid = str(getattr(ds, "SOPClassUID", "") or "")
        if not sop_uid or not sop_class_uid:
            raise CropSeriesError(f"Missing SOP identifiers in {path.name}")
        if sop_uid in seen_sops:
            raise CropSeriesError(f"Duplicate SOP Instance UID {sop_uid}")
        seen_sops.add(sop_uid)
        slices.append(
            _Slice(
                path=path,
                position=float(np.dot(ipp, normal)),
                sop_instance_uid=sop_uid,
                sop_class_uid=sop_class_uid,
                image_position=ipp,
                row_direction=iop[:3],
                column_direction=iop[3:],
                pixel_spacing=pixel_spacing,
                rows=rows,
                columns=columns,
            )
        )

    slices.sort(key=lambda item: item.position)
    for previous, current in zip(slices, slices[1:]):
        if abs(current.position - previous.position) < 1e-4:
            raise CropSeriesError("Image series contains duplicate slice positions")
    return slices, normal


def _contour_points(contour: Dataset) -> np.ndarray:
    values = getattr(contour, "ContourData", None)
    if not values:
        raise CropSeriesError("A contour has no ContourData")
    if len(values) % 3:
        raise CropSeriesError("ContourData length is not a multiple of three")
    try:
        points = np.asarray([float(value) for value in values], dtype=float).reshape((-1, 3))
    except (TypeError, ValueError) as exc:
        raise CropSeriesError("ContourData contains invalid coordinates") from exc
    if not np.all(np.isfinite(points)):
        raise CropSeriesError("ContourData contains non-finite coordinates")
    return points


def _find_matching_contour(
    rtstruct: Dataset, suffix: str
) -> tuple[str, Dataset] | tuple[None, None]:
    names: dict[int, str] = {}
    for roi in getattr(rtstruct, "StructureSetROISequence", []):
        number = getattr(roi, "ROINumber", None)
        name = str(getattr(roi, "ROIName", "") or "")
        try:
            number = int(number)
        except (TypeError, ValueError):
            continue
        if name.casefold().endswith(suffix.casefold()):
            names[number] = name

    matches: list[tuple[str, Dataset]] = []
    for roi_contour in getattr(rtstruct, "ROIContourSequence", []):
        try:
            number = int(getattr(roi_contour, "ReferencedROINumber", None))
        except (TypeError, ValueError):
            continue
        if number not in names:
            continue
        contours = getattr(roi_contour, "ContourSequence", None)
        if contours and any(getattr(contour, "ContourData", None) for contour in contours):
            matches.append((names[number], roi_contour))

    if len(matches) == 1:
        return matches[0]
    return None, None


def _matching_roi_count(rtstruct: Dataset, suffix: str) -> int:
    matching_numbers: set[int] = set()
    for roi in getattr(rtstruct, "StructureSetROISequence", []):
        name = str(getattr(roi, "ROIName", "") or "")
        if not name.casefold().endswith(suffix.casefold()):
            continue
        try:
            matching_numbers.add(int(getattr(roi, "ROINumber", None)))
        except (TypeError, ValueError):
            continue

    count = 0
    for roi_contour in getattr(rtstruct, "ROIContourSequence", []):
        try:
            number = int(getattr(roi_contour, "ReferencedROINumber", None))
        except (TypeError, ValueError):
            continue
        contours = getattr(roi_contour, "ContourSequence", None)
        if (
            number in matching_numbers
            and contours
            and any(getattr(contour, "ContourData", None) for contour in contours)
        ):
            count += 1
    return count


def _nearest_slice_index(positions: np.ndarray, position: float) -> int:
    return int(np.argmin(np.abs(positions - position)))


def _series_coverage(positions: np.ndarray) -> tuple[float, float]:
    if len(positions) == 1:
        return float(positions[0] - 1e-3), float(positions[0] + 1e-3)
    lower = positions[0] - (positions[1] - positions[0]) / 2.0
    upper = positions[-1] + (positions[-1] - positions[-2]) / 2.0
    return float(lower), float(upper)


def _target_in_plane_status(
    roi_contour: Dataset,
    *,
    slices: list[_Slice],
    normal: np.ndarray,
    crop_pixels: int,
) -> str | None:
    """Classify whether the target fits the original and proposed crop FOVs."""

    positions = np.asarray([item.position for item in slices], dtype=float)
    rows = slices[0].rows
    columns = slices[0].columns
    minimum_index = crop_pixels - 0.5
    maximum_column = columns - crop_pixels - 0.5
    maximum_row = rows - crop_pixels - 0.5
    tolerance = 1e-3
    outside_reduced_fov = False

    for contour in roi_contour.ContourSequence:
        points = _contour_points(contour)
        contour_position = float(np.mean(points @ normal))
        slice_info = slices[_nearest_slice_index(positions, contour_position)]
        offsets = points - slice_info.image_position
        column_indices = (
            offsets @ slice_info.row_direction
        ) / slice_info.pixel_spacing[1]
        row_indices = (
            offsets @ slice_info.column_direction
        ) / slice_info.pixel_spacing[0]
        if (
            np.min(column_indices) < -0.5 - tolerance
            or np.max(column_indices) > columns - 0.5 + tolerance
            or np.min(row_indices) < -0.5 - tolerance
            or np.max(row_indices) > rows - 0.5 + tolerance
        ):
            return "outside_original_fov"
        if (
            np.min(column_indices) < minimum_index - tolerance
            or np.max(column_indices) > maximum_column + tolerance
            or np.min(row_indices) < minimum_index - tolerance
            or np.max(row_indices) > maximum_row + tolerance
        ):
            outside_reduced_fov = True

    if outside_reduced_fov:
        return "outside_reduced_fov"
    return None


def _image_reference(
    slice_info: _Slice,
    sop_uid_map: dict[str, str],
) -> Dataset:
    reference = Dataset()
    reference.ReferencedSOPClassUID = slice_info.sop_class_uid
    reference.ReferencedSOPInstanceUID = sop_uid_map[slice_info.sop_instance_uid]
    return reference


def _update_rtstruct_references(
    rtstruct: Dataset,
    *,
    source_series_uid: str,
    derived_series_uid: str,
    sop_uid_map: dict[str, str],
    slices: list[_Slice],
    normal: np.ndarray,
    first_kept: int,
    last_kept: int,
    bind_unreferenced_contours: bool,
) -> Dataset:
    updated = copy.deepcopy(rtstruct)
    retained = slices[first_kept:last_kept + 1]
    retained_references = Sequence(
        [_image_reference(item, sop_uid_map) for item in retained]
    )
    source_sop_uids = {item.sop_instance_uid for item in slices}

    matched_series = False
    for frame in getattr(updated, "ReferencedFrameOfReferenceSequence", []):
        for study in getattr(frame, "RTReferencedStudySequence", []):
            for series in getattr(study, "RTReferencedSeriesSequence", []):
                if str(getattr(series, "SeriesInstanceUID", "")) != str(
                    source_series_uid
                ):
                    continue
                series.SeriesInstanceUID = derived_series_uid
                series.ContourImageSequence = copy.deepcopy(retained_references)
                matched_series = True
    if not matched_series:
        raise CropSeriesError(
            "RTSTRUCT does not contain a referenced-series entry for "
            f"{source_series_uid}"
        )

    positions = np.asarray([item.position for item in slices], dtype=float)
    coverage_start, coverage_end = _series_coverage(positions)
    for roi_contour in getattr(updated, "ROIContourSequence", []):
        contours = getattr(roi_contour, "ContourSequence", None)
        if contours is None:
            continue
        kept_contours = []
        for contour in contours:
            contour_references = getattr(contour, "ContourImageSequence", None)
            referenced_source = bool(
                contour_references
                and any(
                    str(getattr(reference, "ReferencedSOPInstanceUID", ""))
                    in source_sop_uids
                    for reference in contour_references
                )
            )
            if (
                contour_references
                and not referenced_source
                and not bind_unreferenced_contours
            ):
                kept_contours.append(contour)
                continue
            if not contour_references and not bind_unreferenced_contours:
                kept_contours.append(contour)
                continue
            points = _contour_points(contour)
            contour_position = float(np.mean(points @ normal))
            if contour_position < coverage_start or contour_position > coverage_end:
                continue
            index = _nearest_slice_index(positions, contour_position)
            if index < first_kept or index > last_kept:
                continue
            contour.ContourImageSequence = Sequence(
                [_image_reference(slices[index], sop_uid_map)]
            )
            kept_contours.append(contour)
        roi_contour.ContourSequence = Sequence(kept_contours)
    return updated


def _dataset_references_series(
    dataset: Dataset,
    *,
    series_uid: str,
    sop_instance_uids: set[str],
) -> bool:
    """Return whether an RT object contains an image-series reference."""

    for element in dataset.iterall():
        if element.VR == "SQ":
            continue
        if (
            element.keyword == "SeriesInstanceUID"
            and str(element.value) == series_uid
        ):
            return True
        if (
            element.keyword == "ReferencedSOPInstanceUID"
            and str(element.value) in sop_instance_uids
        ):
            return True
    return False


def _rewrite_reference_identifiers(
    dataset: Dataset,
    *,
    source_series_uid: str,
    derived_series_uid: str,
    source_sop_uids: set[str],
    sop_uid_map: dict[str, str],
) -> None:
    """Rewrite retained image references and remove references to deleted slices."""

    for element in list(dataset):
        if element.VR == "SQ":
            retained_items = []
            for item in element.value:
                referenced_uid = str(
                    getattr(item, "ReferencedSOPInstanceUID", "") or ""
                )
                if (
                    referenced_uid in source_sop_uids
                    and referenced_uid not in sop_uid_map
                ):
                    continue
                _rewrite_reference_identifiers(
                    item,
                    source_series_uid=source_series_uid,
                    derived_series_uid=derived_series_uid,
                    source_sop_uids=source_sop_uids,
                    sop_uid_map=sop_uid_map,
                )
                retained_items.append(item)
            element.value = Sequence(retained_items)
        elif (
            element.keyword == "SeriesInstanceUID"
            and str(element.value) == source_series_uid
        ):
            element.value = derived_series_uid
        elif element.keyword == "ReferencedSOPInstanceUID":
            replacement = sop_uid_map.get(str(element.value))
            if replacement:
                element.value = replacement


def _load_affected_reference_objects(
    directory: str,
    *,
    source_series_uid: str,
    slices: list[_Slice],
) -> dict[Path, Dataset]:
    """Load all RTSTRUCT/REG objects that reference the source image series."""

    source_sop_uids = {item.sop_instance_uid for item in slices}
    affected: dict[Path, Dataset] = {}
    for path in Path(directory).rglob("*.dcm"):
        try:
            dataset = pydicom.dcmread(str(path), stop_before_pixels=True)
        except Exception:
            continue
        if str(getattr(dataset, "Modality", "")) not in {"RTSTRUCT", "REG"}:
            continue
        if _dataset_references_series(
            dataset,
            series_uid=source_series_uid,
            sop_instance_uids=source_sop_uids,
        ):
            affected[path.resolve()] = dataset
    return affected


def _stage_cropped_slices(
    slices: list[_Slice],
    *,
    crop_pixels: int,
    derived_series_uid: str,
    sop_uid_map: dict[str, str],
    staging_directory: Path,
) -> list[tuple[Path, Path]]:
    staged: list[tuple[Path, Path]] = []
    for index, slice_info in enumerate(slices):
        ds = pydicom.dcmread(str(slice_info.path))
        if "PixelData" not in ds:
            raise CropSeriesError(f"Missing PixelData in {slice_info.path.name}")
        pixels = ds.pixel_array
        if pixels.ndim != 2 or pixels.shape != (slice_info.rows, slice_info.columns):
            raise CropSeriesError(
                f"Unexpected pixel-array dimensions in {slice_info.path.name}"
            )
        cropped = np.ascontiguousarray(
            pixels[crop_pixels:-crop_pixels, crop_pixels:-crop_pixels]
        )
        if cropped.size == 0:
            raise CropSeriesError("The in-plane crop produced an empty image")

        ds.Rows, ds.Columns = cropped.shape
        pixel_bytes = cropped.tobytes()
        if len(pixel_bytes) % 2:
            pixel_bytes += b"\0"
        ds.PixelData = pixel_bytes

        shifted_position = (
            slice_info.image_position
            + crop_pixels
            * slice_info.pixel_spacing[1]
            * slice_info.row_direction
            + crop_pixels
            * slice_info.pixel_spacing[0]
            * slice_info.column_direction
        )
        ds.ImagePositionPatient = [f"{value:.10g}" for value in shifted_position]
        ds.SeriesInstanceUID = derived_series_uid
        ds.SOPInstanceUID = sop_uid_map[slice_info.sop_instance_uid]
        ds.file_meta.MediaStorageSOPInstanceUID = ds.SOPInstanceUID
        image_type = [str(value) for value in getattr(ds, "ImageType", [])]
        if image_type:
            image_type[0] = "DERIVED"
        else:
            image_type = ["DERIVED", "SECONDARY"]
        ds.ImageType = image_type
        ds.DerivationDescription = "Cropped from source image series"
        if "SmallestImagePixelValue" in ds:
            ds.SmallestImagePixelValue = int(np.min(cropped))
        if "LargestImagePixelValue" in ds:
            ds.LargestImagePixelValue = int(np.max(cropped))

        staged_path = staging_directory / f"image_{index:06d}.tmp"
        pydicom.dcmwrite(staged_path, ds, write_like_original=False)
        staged.append((staged_path, slice_info.path))
    return staged


def _commit_staged_crop(
    *,
    staged_files: list[tuple[Path, Path]],
    files_to_delete: list[Path],
    working_directory: str,
) -> None:
    backup_directory = Path(
        tempfile.mkdtemp(prefix=".crop_backup_", dir=working_directory)
    )
    replacements = staged_files
    originals = [*[path for _, path in staged_files], *files_to_delete]
    moved: list[tuple[Path, Path]] = []
    preserve_backup = False
    try:
        for index, original in enumerate(originals):
            backup = backup_directory / f"original_{index:06d}"
            os.replace(original, backup)
            moved.append((backup, original))
        for staged_path, destination in replacements:
            os.replace(staged_path, destination)
    except Exception as exc:
        rollback_errors = []
        for backup, original in reversed(moved):
            try:
                os.replace(backup, original)
            except Exception as rollback_exc:
                rollback_errors.append(str(rollback_exc))
        detail = f"Failed to commit cropped DICOM files: {exc}"
        if rollback_errors:
            preserve_backup = True
            detail += "; rollback errors: " + "; ".join(rollback_errors)
        raise CropSeriesError(detail) from exc
    finally:
        if not preserve_backup:
            shutil.rmtree(backup_directory, ignore_errors=True)


def crop_registered_series(
    current_directory: str,
    series_uid: str,
    rtstruct_path: str,
    *,
    roi_suffix: str = "+2cm_Ph",
    padding_slices: int = 2,
    in_plane_crop_pixels: int = 96,
) -> CropResult:
    """Crop an eligible sCT MR *series_uid* to a matching contoured ROI.

    Only MR series whose Series Description starts with
    ``sCT_sp_Pel_T2`` are eligible. Ineligible series and ambiguous or absent
    matching contours are deliberately treated as safe no-ops. Geometry,
    write, and deletion failures return ``status="failed"``.
    """

    try:
        if padding_slices < 0:
            raise CropSeriesError("padding_slices must be non-negative")
        if in_plane_crop_pixels <= 0:
            raise CropSeriesError("in_plane_crop_pixels must be positive")

        rtstruct = pydicom.dcmread(rtstruct_path)
        matching_count = _matching_roi_count(rtstruct, roi_suffix)
        roi_name, roi_contour = _find_matching_contour(rtstruct, roi_suffix)
        if matching_count != 1 or roi_contour is None:
            warning = (
                f"Expected one contoured ROI ending with '{roi_suffix}', "
                f"found {matching_count}; leaving series unchanged"
            )
            return CropResult(status="skipped", warning=warning)

        slices, normal = _load_series_slices(current_directory, series_uid)
        original_rows = slices[0].rows
        original_columns = slices[0].columns
        cropped_rows = original_rows - 2 * in_plane_crop_pixels
        cropped_columns = original_columns - 2 * in_plane_crop_pixels
        if cropped_rows <= 0 or cropped_columns <= 0:
            raise CropSeriesError(
                "Image Rows and Columns must both exceed twice the in-plane crop"
            )
        positions = np.asarray([item.position for item in slices], dtype=float)
        projected_points = []
        for contour in roi_contour.ContourSequence:
            projected_points.extend((_contour_points(contour) @ normal).tolist())
        if not projected_points:
            warning = (
                f"ROI '{roi_name}' has no contour points; leaving series unchanged"
            )
            return CropResult(status="skipped", roi_name=roi_name, warning=warning)

        coverage_start, coverage_end = _series_coverage(positions)
        target_start = min(projected_points)
        target_end = max(projected_points)
        if target_start < coverage_start or target_end > coverage_end:
            lower_missing = max(0.0, coverage_start - target_start)
            upper_missing = max(0.0, target_end - coverage_end)
            if normal[2] >= 0:
                caudal_missing, cranial_missing = lower_missing, upper_missing
            else:
                caudal_missing, cranial_missing = upper_missing, lower_missing
            warning = (
                f"ROI '{roi_name}' extends outside the referenced image series; "
                "leaving the image series unchanged"
            )
            return CropResult(
                status="skipped",
                roi_name=roi_name,
                source_series_uid=str(series_uid),
                warning=warning,
                warning_code="insufficient_longitudinal_coverage",
                caudal_missing_mm=caudal_missing,
                cranial_missing_mm=cranial_missing,
            )
        in_plane_status = _target_in_plane_status(
            roi_contour,
            slices=slices,
            normal=normal,
            crop_pixels=in_plane_crop_pixels,
        )
        if in_plane_status is not None:
            if in_plane_status == "outside_original_fov":
                warning_code = "insufficient_in_plane_coverage"
                warning = (
                    f"ROI '{roi_name}' extends outside the acquired in-plane "
                    "field of view; leaving the image series unchanged"
                )
            else:
                warning_code = "insufficient_in_plane_crop_margin"
                warning = (
                    f"ROI '{roi_name}' extends outside the proposed reduced "
                    "in-plane field of view; leaving the image series unchanged"
                )
            return CropResult(
                status="skipped",
                roi_name=roi_name,
                original_rows=original_rows,
                original_columns=original_columns,
                source_series_uid=str(series_uid),
                warning=warning,
                warning_code=warning_code,
            )

        first_contour = _nearest_slice_index(positions, min(projected_points))
        last_contour = _nearest_slice_index(positions, max(projected_points))
        first_kept = max(0, min(first_contour, last_contour) - padding_slices)
        last_kept = min(
            len(slices) - 1,
            max(first_contour, last_contour) + padding_slices,
        )
        if first_kept > last_kept:
            raise CropSeriesError("Calculated crop range is empty")

        to_delete = slices[:first_kept] + slices[last_kept + 1:]
        retained_slices = slices[first_kept:last_kept + 1]
        derived_series_uid = generate_uid()
        sop_uid_map = {
            item.sop_instance_uid: generate_uid() for item in retained_slices
        }
        source_sop_uids = {item.sop_instance_uid for item in slices}
        affected_objects = _load_affected_reference_objects(
            current_directory,
            source_series_uid=series_uid,
            slices=slices,
        )
        resolved_rtstruct_path = Path(rtstruct_path).resolve()
        if resolved_rtstruct_path not in affected_objects:
            raise CropSeriesError(
                f"RTSTRUCT does not reference source image series {series_uid}"
            )

        staging_directory = Path(
            tempfile.mkdtemp(prefix=".crop_staging_", dir=current_directory)
        )
        try:
            staged_reference_files = []
            for index, (path, dataset) in enumerate(affected_objects.items()):
                if str(getattr(dataset, "Modality", "")) == "RTSTRUCT":
                    updated = _update_rtstruct_references(
                        dataset,
                        source_series_uid=series_uid,
                        derived_series_uid=derived_series_uid,
                        sop_uid_map=sop_uid_map,
                        slices=slices,
                        normal=normal,
                        first_kept=first_kept,
                        last_kept=last_kept,
                        bind_unreferenced_contours=path == resolved_rtstruct_path,
                    )
                else:
                    updated = copy.deepcopy(dataset)
                _rewrite_reference_identifiers(
                    updated,
                    source_series_uid=series_uid,
                    derived_series_uid=derived_series_uid,
                    source_sop_uids=source_sop_uids,
                    sop_uid_map=sop_uid_map,
                )
                staged_path = staging_directory / f"reference_{index:06d}.tmp"
                pydicom.dcmwrite(staged_path, updated, write_like_original=False)
                staged_reference_files.append((staged_path, path))

            staged_files = _stage_cropped_slices(
                retained_slices,
                crop_pixels=in_plane_crop_pixels,
                derived_series_uid=derived_series_uid,
                sop_uid_map=sop_uid_map,
                staging_directory=staging_directory,
            )
            _commit_staged_crop(
                staged_files=[*staged_reference_files, *staged_files],
                files_to_delete=[item.path for item in to_delete],
                working_directory=current_directory,
            )
        finally:
            shutil.rmtree(staging_directory, ignore_errors=True)

        retained_count = last_kept - first_kept + 1
        return CropResult(
            status="cropped",
            retained_count=retained_count,
            deleted_count=len(to_delete),
            roi_name=roi_name,
            original_rows=original_rows,
            original_columns=original_columns,
            cropped_rows=cropped_rows,
            cropped_columns=cropped_columns,
            source_series_uid=str(series_uid),
            derived_series_uid=derived_series_uid,
        )
    except IneligibleCropSeriesError as exc:
        return CropResult(
            status="skipped",
            source_series_uid=str(series_uid),
            warning=str(exc),
            warning_code="ineligible_series_for_crop",
        )
    except Exception as exc:
        return CropResult(status="failed", error=str(exc))


def copy_structures_and_crop(
    current_directory: str,
    patient_id: str,
    rtplan_label: str,
    rigid_transform,
    *,
    series_uid: str | None,
    base_series_uid: str | None,
    progress_callback=None,
) -> CropResult:
    """Copy transformed structures, then crop their registered image series.

    A skipped crop is a successful copy-only outcome. A failed crop, or an
    exception raised while attempting it, restores the target RTSTRUCT exactly
    as it was before the copy (or removes an RTSTRUCT created by the copy).
    """

    current_path = Path(current_directory)
    rtstruct_snapshots: dict[Path, bytes] = {}
    canonical_target = (
        (current_path / f"RS_{series_uid}.dcm").resolve()
        if series_uid
        else None
    )
    for candidate in current_path.iterdir():
        if not candidate.is_file():
            continue
        resolved_candidate = candidate.resolve()
        if resolved_candidate == canonical_target:
            rtstruct_snapshots[resolved_candidate] = candidate.read_bytes()
            continue
        try:
            dataset = pydicom.dcmread(candidate, stop_before_pixels=True)
        except Exception:
            continue
        if str(getattr(dataset, "Modality", "")) == "RTSTRUCT":
            rtstruct_snapshots[resolved_candidate] = candidate.read_bytes()

    rtstruct_path = copy_structures(
        current_directory,
        patient_id,
        rtplan_label,
        rigid_transform,
        series_uid=series_uid,
        base_series_uid=base_series_uid,
        progress_callback=progress_callback,
    )
    resolved_rtstruct_path = Path(rtstruct_path).resolve()

    def restore_pre_copy_rtstruct() -> None:
        original = rtstruct_snapshots.get(resolved_rtstruct_path)
        if original is None:
            resolved_rtstruct_path.unlink(missing_ok=True)
            return

        descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{resolved_rtstruct_path.name}.rollback_",
            dir=resolved_rtstruct_path.parent,
        )
        temporary_path = Path(temporary_name)
        try:
            with os.fdopen(descriptor, "wb") as temporary_file:
                temporary_file.write(original)
                temporary_file.flush()
                os.fsync(temporary_file.fileno())
            os.replace(temporary_path, resolved_rtstruct_path)
        finally:
            temporary_path.unlink(missing_ok=True)

    try:
        if not series_uid:
            result = CropResult(
                status="failed",
                error="Cannot crop registered image series without a Series Instance UID",
            )
        else:
            result = crop_registered_series(
                current_directory,
                series_uid,
                rtstruct_path,
            )
    except Exception:
        restore_pre_copy_rtstruct()
        raise

    if result.status not in {"cropped", "skipped"}:
        try:
            restore_pre_copy_rtstruct()
        except Exception as rollback_exc:
            raise RuntimeError(
                f"Image-series crop failed: {result.error}; "
                f"failed to restore pre-copy RTSTRUCT: {rollback_exc}"
            ) from rollback_exc

    if result.status == "cropped":
        print(
            f"{get_datetime()} Cropped series to ROI '{result.roi_name}': "
            f"retained {result.retained_count}, deleted {result.deleted_count} slices; "
            f"FOV {result.original_columns}x{result.original_rows} -> "
            f"{result.cropped_columns}x{result.cropped_rows} pixels"
        )
    elif result.status == "skipped":
        print(f"{get_datetime()} Crop skipped: {result.warning}")
    else:
        raise RuntimeError(f"Image-series crop failed: {result.error}")
    return result
