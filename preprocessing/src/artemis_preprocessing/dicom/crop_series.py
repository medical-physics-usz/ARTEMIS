"""Crop a DICOM image series to the slice extent of an RTSTRUCT ROI."""

from __future__ import annotations

import copy
import os
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import pydicom
from pydicom.dataset import Dataset
from pydicom.sequence import Sequence

from artemis_preprocessing.dicom.copy_structures import copy_structures
from artemis_preprocessing.utils import get_datetime


@dataclass(frozen=True)
class CropResult:
    """Outcome of an attempted registered-series crop."""

    status: str
    retained_count: int = 0
    deleted_count: int = 0
    roi_name: str | None = None
    warning: str | None = None
    error: str | None = None

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass(frozen=True)
class _Slice:
    path: Path
    position: float
    sop_instance_uid: str
    sop_class_uid: str


class CropSeriesError(RuntimeError):
    """Raised internally when a crop cannot be completed safely."""


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

        ipp = _as_vector(
            getattr(ds, "ImagePositionPatient", None),
            length=3,
            field=f"ImagePositionPatient in {path.name}",
        )
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


def _image_reference(slice_info: _Slice) -> Dataset:
    reference = Dataset()
    reference.ReferencedSOPClassUID = slice_info.sop_class_uid
    reference.ReferencedSOPInstanceUID = slice_info.sop_instance_uid
    return reference


def _update_rtstruct_references(
    rtstruct: Dataset,
    *,
    series_uid: str,
    slices: list[_Slice],
    normal: np.ndarray,
    first_kept: int,
    last_kept: int,
) -> Dataset:
    updated = copy.deepcopy(rtstruct)
    retained = slices[first_kept:last_kept + 1]
    retained_references = Sequence([_image_reference(item) for item in retained])

    matched_series = False
    for frame in getattr(updated, "ReferencedFrameOfReferenceSequence", []):
        for study in getattr(frame, "RTReferencedStudySequence", []):
            for series in getattr(study, "RTReferencedSeriesSequence", []):
                if str(getattr(series, "SeriesInstanceUID", "")) != str(series_uid):
                    continue
                series.ContourImageSequence = copy.deepcopy(retained_references)
                matched_series = True
    if not matched_series:
        raise CropSeriesError(
            f"RTSTRUCT does not contain a referenced-series entry for {series_uid}"
        )

    positions = np.asarray([item.position for item in slices], dtype=float)
    coverage_start, coverage_end = _series_coverage(positions)
    for roi_contour in getattr(updated, "ROIContourSequence", []):
        contours = getattr(roi_contour, "ContourSequence", None)
        if contours is None:
            continue
        kept_contours = []
        for contour in contours:
            points = _contour_points(contour)
            contour_position = float(np.mean(points @ normal))
            if contour_position < coverage_start or contour_position > coverage_end:
                continue
            index = _nearest_slice_index(positions, contour_position)
            if index < first_kept or index > last_kept:
                continue
            contour.ContourImageSequence = Sequence([_image_reference(slices[index])])
            kept_contours.append(contour)
        roi_contour.ContourSequence = Sequence(kept_contours)
    return updated


def _write_rtstruct_safely(rtstruct: Dataset, rtstruct_path: str) -> None:
    destination = Path(rtstruct_path)
    temp_path: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            prefix=f".{destination.name}.",
            suffix=".tmp",
            dir=str(destination.parent),
            delete=False,
        ) as temp_file:
            temp_path = temp_file.name
        pydicom.dcmwrite(temp_path, rtstruct, write_like_original=False)
        os.replace(temp_path, destination)
        temp_path = None
    finally:
        if temp_path and os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass


def crop_registered_series(
    current_directory: str,
    series_uid: str,
    rtstruct_path: str,
    *,
    roi_suffix: str = "+2cm_Ph",
    padding_slices: int = 2,
) -> CropResult:
    """Crop *series_uid* to a uniquely matching contoured ROI.

    Ambiguous or absent matching contours are deliberately treated as a safe
    no-op. Geometry, write, and deletion failures return ``status="failed"``.
    """

    try:
        if padding_slices < 0:
            raise CropSeriesError("padding_slices must be non-negative")

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
        if min(projected_points) < coverage_start or max(projected_points) > coverage_end:
            raise CropSeriesError(
                f"ROI '{roi_name}' extends outside the referenced image series"
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

        updated_rtstruct = _update_rtstruct_references(
            rtstruct,
            series_uid=series_uid,
            slices=slices,
            normal=normal,
            first_kept=first_kept,
            last_kept=last_kept,
        )
        to_delete = slices[:first_kept] + slices[last_kept + 1:]
        _write_rtstruct_safely(updated_rtstruct, rtstruct_path)
        for item in to_delete:
            os.remove(item.path)

        retained_count = last_kept - first_kept + 1
        return CropResult(
            status="cropped",
            retained_count=retained_count,
            deleted_count=len(to_delete),
            roi_name=roi_name,
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
    """Copy transformed structures, then crop their registered image series."""

    rtstruct_path = copy_structures(
        current_directory,
        patient_id,
        rtplan_label,
        rigid_transform,
        series_uid=series_uid,
        base_series_uid=base_series_uid,
        progress_callback=progress_callback,
    )
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

    if result.status == "cropped":
        print(
            f"{get_datetime()} Cropped series to ROI '{result.roi_name}': "
            f"retained {result.retained_count}, deleted {result.deleted_count} slices"
        )
    elif result.status == "skipped":
        print(f"{get_datetime()} Crop skipped: {result.warning}")
    else:
        raise RuntimeError(f"Image-series crop failed: {result.error}")
    return result
