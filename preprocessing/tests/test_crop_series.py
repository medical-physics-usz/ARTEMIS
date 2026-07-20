from __future__ import annotations

import hashlib
import math
import os
from pathlib import Path

import numpy as np
import pydicom
import pytest
from pydicom.dataset import Dataset, FileDataset, FileMetaDataset
from pydicom.encaps import encapsulate
from pydicom.sequence import Sequence
from pydicom.uid import (
    CTImageStorage,
    ExplicitVRLittleEndian,
    JPEGBaseline8Bit,
    MRImageStorage,
    RTStructureSetStorage,
    generate_uid,
)

from artemis_preprocessing.dicom import crop_series
from artemis_preprocessing.dicom.crop_series import crop_registered_series


def _file_dataset(path: Path, sop_class_uid: str, sop_instance_uid: str) -> FileDataset:
    file_meta = FileMetaDataset()
    file_meta.TransferSyntaxUID = ExplicitVRLittleEndian
    file_meta.MediaStorageSOPClassUID = sop_class_uid
    file_meta.MediaStorageSOPInstanceUID = sop_instance_uid
    ds = FileDataset(str(path), {}, file_meta=file_meta, preamble=b"\0" * 128)
    ds.SOPClassUID = sop_class_uid
    ds.SOPInstanceUID = sop_instance_uid
    return ds


def _write_image_series(
    directory: Path,
    *,
    count: int = 10,
    series_uid: str | None = None,
    prefix: str = "CT",
    iop=(1, 0, 0, 0, 1, 0),
    spacing: float = 1.0,
    reverse_instances: bool = False,
    rows: int = 256,
    columns: int = 256,
    pixel_spacing=(1.0, 1.0),
    signed: bool = True,
    modality: str = "CT",
) -> tuple[str, list[tuple[Path, str, np.ndarray]]]:
    series_uid = series_uid or generate_uid()
    row = np.asarray(iop[:3], dtype=float)
    column = np.asarray(iop[3:], dtype=float)
    normal = np.cross(row, column)
    records = []
    for index in range(count):
        path = directory / f"{prefix}_{index:03d}.dcm"
        sop_uid = generate_uid()
        sop_class_uid = MRImageStorage if modality == "MR" else CTImageStorage
        ds = _file_dataset(path, sop_class_uid, sop_uid)
        ds.Modality = modality
        ds.SeriesInstanceUID = series_uid
        ds.StudyInstanceUID = generate_uid()
        ds.FrameOfReferenceUID = generate_uid()
        ds.ImageOrientationPatient = [f"{value:.10g}" for value in iop]
        position = (
            normal * spacing * index
            - row * pixel_spacing[1] * columns / 2
            - column * pixel_spacing[0] * rows / 2
        )
        ds.ImagePositionPatient = [f"{value:.10g}" for value in position]
        ds.PixelSpacing = [f"{value:.10g}" for value in pixel_spacing]
        ds.Rows = rows
        ds.Columns = columns
        ds.SamplesPerPixel = 1
        ds.PhotometricInterpretation = "MONOCHROME2"
        ds.BitsAllocated = 16
        ds.BitsStored = 16
        ds.HighBit = 15
        ds.PixelRepresentation = int(signed)
        dtype = np.int16 if signed else np.uint16
        pixels = np.arange(rows * columns, dtype=dtype).reshape(rows, columns)
        pixels = pixels + index
        ds.PixelData = pixels.tobytes()
        ds.InstanceNumber = count - index if reverse_instances else index + 1
        pydicom.dcmwrite(path, ds, enforce_file_format=True)
        records.append((path, sop_uid, position))
    return series_uid, records


def _contour(
    position: float,
    *,
    normal=(0, 0, 1),
    row=(1, 0, 0),
    column=(0, 1, 0),
    offset=(0, 0, 0),
) -> Dataset:
    normal = np.asarray(normal, dtype=float)
    row = np.asarray(row, dtype=float)
    column = np.asarray(column, dtype=float)
    center = normal * position + np.asarray(offset, dtype=float)
    points = [
        center - row - column,
        center + row - column,
        center + row + column,
        center - row + column,
    ]
    item = Dataset()
    item.ContourGeometricType = "CLOSED_PLANAR"
    item.NumberOfContourPoints = len(points)
    item.ContourData = [f"{value:.10g}" for point in points for value in point]
    return item


def _write_rtstruct(
    path: Path,
    *,
    series_uid: str,
    image_records,
    rois: list[tuple[str, list[Dataset]]],
) -> Path:
    ds = _file_dataset(path, RTStructureSetStorage, generate_uid())
    ds.Modality = "RTSTRUCT"
    ds.SeriesInstanceUID = generate_uid()
    ds.StudyInstanceUID = generate_uid()
    ds.StructureSetLabel = "TEST"

    frame = Dataset()
    frame.FrameOfReferenceUID = generate_uid()
    study = Dataset()
    referenced_series = Dataset()
    referenced_series.SeriesInstanceUID = series_uid
    referenced_series.ContourImageSequence = Sequence()
    for _, sop_uid, _ in image_records:
        ref = Dataset()
        ref.ReferencedSOPClassUID = CTImageStorage
        ref.ReferencedSOPInstanceUID = sop_uid
        referenced_series.ContourImageSequence.append(ref)
    study.RTReferencedSeriesSequence = Sequence([referenced_series])
    frame.RTReferencedStudySequence = Sequence([study])
    ds.ReferencedFrameOfReferenceSequence = Sequence([frame])

    ds.StructureSetROISequence = Sequence()
    ds.ROIContourSequence = Sequence()
    ds.RTROIObservationsSequence = Sequence()
    for number, (name, contours) in enumerate(rois, 1):
        roi = Dataset()
        roi.ROINumber = number
        roi.ROIName = name
        roi.ReferencedFrameOfReferenceUID = frame.FrameOfReferenceUID
        ds.StructureSetROISequence.append(roi)

        roi_contour = Dataset()
        roi_contour.ReferencedROINumber = number
        roi_contour.ContourSequence = Sequence(contours)
        ds.ROIContourSequence.append(roi_contour)

        observation = Dataset()
        observation.ObservationNumber = number
        observation.ReferencedROINumber = number
        observation.RTROIInterpretedType = "ORGAN"
        ds.RTROIObservationsSequence.append(observation)

    pydicom.dcmwrite(path, ds, enforce_file_format=True)
    return path


def _write_reg(path: Path, *, series_uid: str, image_records) -> Path:
    ds = _file_dataset(path, "1.2.840.10008.5.1.4.1.1.66.1", generate_uid())
    ds.Modality = "REG"
    ds.SeriesInstanceUID = generate_uid()
    ds.StudyInstanceUID = generate_uid()

    referenced_series = Dataset()
    referenced_series.SeriesInstanceUID = series_uid
    referenced_series.ReferencedInstanceSequence = Sequence()
    for _, sop_uid, _ in image_records:
        reference = Dataset()
        reference.ReferencedSOPClassUID = CTImageStorage
        reference.ReferencedSOPInstanceUID = sop_uid
        referenced_series.ReferencedInstanceSequence.append(reference)
    ds.ReferencedSeriesSequence = Sequence([referenced_series])

    registration = Dataset()
    registration.ReferencedImageSequence = Sequence()
    for _, sop_uid, _ in image_records:
        reference = Dataset()
        reference.ReferencedSOPClassUID = CTImageStorage
        reference.ReferencedSOPInstanceUID = sop_uid
        registration.ReferencedImageSequence.append(reference)
    ds.RegistrationSequence = Sequence([registration])
    pydicom.dcmwrite(path, ds, enforce_file_format=True)
    return path


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _snapshot(directory: Path) -> dict[str, str]:
    return {
        path.name: _sha256(path)
        for path in directory.glob("*.dcm")
    }


def _series_files(directory: Path, series_uid: str) -> list[Path]:
    result = []
    for path in directory.glob("*.dcm"):
        ds = pydicom.dcmread(path, stop_before_pixels=True)
        if str(getattr(ds, "SeriesInstanceUID", "")) == series_uid:
            result.append(path)
    return result


@pytest.mark.parametrize("reverse_instances", [False, True])
def test_crop_keeps_roi_extent_padding_and_updates_references(
    tmp_path: Path, reverse_instances: bool
):
    series_uid, images = _write_image_series(
        tmp_path, reverse_instances=reverse_instances
    )
    other_uid, other_images = _write_image_series(
        tmp_path, count=3, prefix="OTHER"
    )
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_test.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[
            ("PTV_TARGET+2CM_pH", [_contour(3), _contour(6)]),
            ("Organ", [_contour(0), _contour(4), _contour(9)]),
        ],
    )

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "cropped"
    assert result.roi_name == "PTV_TARGET+2CM_pH"
    assert result.retained_count == 8
    assert result.deleted_count == 2
    assert (result.original_rows, result.original_columns) == (256, 256)
    assert (result.cropped_rows, result.cropped_columns) == (64, 64)
    assert result.source_series_uid == series_uid
    assert result.derived_series_uid
    assert result.derived_series_uid != series_uid
    assert len(_series_files(tmp_path, series_uid)) == 0
    assert len(_series_files(tmp_path, result.derived_series_uid)) == 8
    assert len(_series_files(tmp_path, other_uid)) == len(other_images) == 3

    updated = pydicom.dcmread(rtstruct_path)
    ref_series = (
        updated.ReferencedFrameOfReferenceSequence[0]
        .RTReferencedStudySequence[0]
        .RTReferencedSeriesSequence[0]
    )
    retained_uids = {
        str(item.ReferencedSOPInstanceUID)
        for item in ref_series.ContourImageSequence
    }
    original_retained_uids = {images[index][1] for index in range(1, 9)}
    assert retained_uids.isdisjoint(original_retained_uids)
    assert len(retained_uids) == 8
    assert str(ref_series.SeriesInstanceUID) == result.derived_series_uid

    retained_image = pydicom.dcmread(images[4][0])
    original_pixels = (
        np.arange(256 * 256, dtype=np.int16).reshape(256, 256) + 4
    )
    assert retained_image.pixel_array.shape == (64, 64)
    np.testing.assert_array_equal(
        retained_image.pixel_array,
        original_pixels[96:-96, 96:-96],
    )
    np.testing.assert_allclose(
        [float(value) for value in retained_image.ImagePositionPatient],
        [-32.0, -32.0, 4.0],
    )
    assert str(retained_image.SOPInstanceUID) != images[4][1]
    assert (
        str(retained_image.file_meta.MediaStorageSOPInstanceUID)
        == str(retained_image.SOPInstanceUID)
    )
    assert str(retained_image.SeriesInstanceUID) == result.derived_series_uid
    assert retained_image.ImageType[0] == "DERIVED"

    organ_contours = updated.ROIContourSequence[1].ContourSequence
    assert len(organ_contours) == 1
    for roi_contour in updated.ROIContourSequence:
        for contour in roi_contour.ContourSequence:
            assert len(contour.ContourImageSequence) == 1
            assert (
                str(contour.ContourImageSequence[0].ReferencedSOPInstanceUID)
                in retained_uids
            )


def test_crop_updates_every_rtstruct_and_reg_reference(tmp_path: Path):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_target.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(3), _contour(6)])],
    )
    other_rtstruct_path = _write_rtstruct(
        tmp_path / "RS_other.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("Organ", [_contour(0), _contour(4)])],
    )
    other_rtstruct = pydicom.dcmread(other_rtstruct_path)
    for contour, image_index in zip(
        other_rtstruct.ROIContourSequence[0].ContourSequence, [0, 4]
    ):
        reference = Dataset()
        reference.ReferencedSOPClassUID = CTImageStorage
        reference.ReferencedSOPInstanceUID = images[image_index][1]
        contour.ContourImageSequence = Sequence([reference])
    pydicom.dcmwrite(other_rtstruct_path, other_rtstruct, enforce_file_format=True)
    reg_path = _write_reg(
        tmp_path / "REG_test.dcm",
        series_uid=series_uid,
        image_records=images,
    )

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "cropped"
    derived_image_uids = {
        str(pydicom.dcmread(path, stop_before_pixels=True).SOPInstanceUID)
        for path in _series_files(tmp_path, result.derived_series_uid)
    }
    original_image_uids = {record[1] for record in images}
    assert len(derived_image_uids) == 8
    assert derived_image_uids.isdisjoint(original_image_uids)

    for path in [rtstruct_path, other_rtstruct_path]:
        updated_rtstruct = pydicom.dcmread(path)
        referenced_series = (
            updated_rtstruct.ReferencedFrameOfReferenceSequence[0]
            .RTReferencedStudySequence[0]
            .RTReferencedSeriesSequence[0]
        )
        assert str(referenced_series.SeriesInstanceUID) == result.derived_series_uid
        assert {
            str(item.ReferencedSOPInstanceUID)
            for item in referenced_series.ContourImageSequence
        } == derived_image_uids

    updated_other = pydicom.dcmread(other_rtstruct_path)
    remaining_contours = updated_other.ROIContourSequence[0].ContourSequence
    assert len(remaining_contours) == 1
    assert (
        str(remaining_contours[0].ContourImageSequence[0].ReferencedSOPInstanceUID)
        in derived_image_uids
    )

    updated_reg = pydicom.dcmread(reg_path)
    assert (
        str(updated_reg.ReferencedSeriesSequence[0].SeriesInstanceUID)
        == result.derived_series_uid
    )
    for sequence in [
        updated_reg.ReferencedSeriesSequence[0].ReferencedInstanceSequence,
        updated_reg.RegistrationSequence[0].ReferencedImageSequence,
    ]:
        assert {str(item.ReferencedSOPInstanceUID) for item in sequence} == (
            derived_image_uids
        )


def test_crop_rebinds_primary_base_series_contour_references_only(tmp_path: Path):
    series_uid, images = _write_image_series(tmp_path)
    _, base_images = _write_image_series(tmp_path, prefix="BASE")
    primary_path = _write_rtstruct(
        tmp_path / "RS_primary.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(3), _contour(6)])],
    )
    secondary_path = _write_rtstruct(
        tmp_path / "RS_secondary.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("Organ", [_contour(4)])],
    )

    for path, image_indices in [(primary_path, [3, 6]), (secondary_path, [4])]:
        rtstruct = pydicom.dcmread(path)
        for contour, image_index in zip(
            rtstruct.ROIContourSequence[0].ContourSequence, image_indices
        ):
            reference = Dataset()
            reference.ReferencedSOPClassUID = CTImageStorage
            reference.ReferencedSOPInstanceUID = base_images[image_index][1]
            contour.ContourImageSequence = Sequence([reference])
        pydicom.dcmwrite(path, rtstruct, enforce_file_format=True)

    result = crop_registered_series(str(tmp_path), series_uid, str(primary_path))

    assert result.status == "cropped"
    expected_primary_references = [
        str(
            pydicom.dcmread(
                images[index][0], stop_before_pixels=True
            ).SOPInstanceUID
        )
        for index in [3, 6]
    ]
    updated_primary = pydicom.dcmread(primary_path)
    assert [
        str(contour.ContourImageSequence[0].ReferencedSOPInstanceUID)
        for contour in updated_primary.ROIContourSequence[0].ContourSequence
    ] == expected_primary_references

    updated_secondary = pydicom.dcmread(secondary_path)
    secondary_reference = (
        updated_secondary.ROIContourSequence[0]
        .ContourSequence[0]
        .ContourImageSequence[0]
        .ReferencedSOPInstanceUID
    )
    assert str(secondary_reference) == base_images[4][1]


def test_crop_uses_oblique_slice_geometry(tmp_path: Path):
    angle = math.radians(30)
    iop = (1, 0, 0, 0, math.cos(angle), math.sin(angle))
    row = np.asarray(iop[:3])
    column = np.asarray(iop[3:])
    normal = np.cross(row, column)
    series_uid, images = _write_image_series(
        tmp_path, count=9, iop=iop, spacing=2.5
    )
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_oblique.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[
            (
                "PTV+2cm_Ph",
                [
                    _contour(2.5 * 3, normal=normal, row=row, column=column),
                    _contour(2.5 * 4, normal=normal, row=row, column=column),
                ],
            )
        ],
    )

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "cropped"
    assert result.retained_count == 6
    assert result.deleted_count == 3
    assert not _series_files(tmp_path, series_uid)
    assert {
        path.name for path in _series_files(tmp_path, result.derived_series_uid)
    } == {
        images[index][0].name for index in range(1, 7)
    }
    cropped_header = pydicom.dcmread(images[3][0], stop_before_pixels=True)
    original_position = images[3][2]
    expected_position = original_position + 96 * row + 96 * column
    np.testing.assert_allclose(
        [float(value) for value in cropped_header.ImagePositionPatient],
        expected_position,
        atol=1e-7,
    )


@pytest.mark.parametrize(
    "rois, expected_count",
    [
        ([("Other", [_contour(3)])], 0),
        ([("PTV+2cm_Ph", [])], 0),
        (
            [
                ("PTV_A+2cm_Ph", [_contour(3)]),
                ("PTV_B+2cm_Ph", [_contour(5)]),
            ],
            2,
        ),
    ],
)
def test_ambiguous_or_missing_contour_skips_without_changes(
    tmp_path: Path, rois, expected_count: int
):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_skip.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=rois,
    )
    original_hash = _sha256(rtstruct_path)
    original_files = {path for path, _, _ in images}

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "skipped"
    assert f"found {expected_count}" in result.warning
    assert _sha256(rtstruct_path) == original_hash
    assert set(_series_files(tmp_path, series_uid)) == original_files


def test_padding_clamps_to_series_boundary(tmp_path: Path):
    series_uid, images = _write_image_series(tmp_path, count=6)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_boundary.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(0), _contour(1)])],
    )

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "cropped"
    assert result.retained_count == 4
    assert result.deleted_count == 2


@pytest.mark.parametrize(
    "iop, contours, expected_caudal, expected_cranial",
    [
        ((1, 0, 0, 0, 1, 0), (-2, 12), 1.5, 2.5),
        ((-1, 0, 0, 0, 1, 0), (-2, 12), 2.5, 1.5),
    ],
)
def test_longitudinal_shortfall_skips_crop_and_reports_patient_directions(
    tmp_path: Path,
    iop,
    contours,
    expected_caudal: float,
    expected_cranial: float,
):
    series_uid, images = _write_image_series(tmp_path, iop=iop)
    normal = np.cross(np.asarray(iop[:3]), np.asarray(iop[3:]))
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_short_coverage.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[
            (
                "PTV+2cm_Ph",
                [
                    _contour(position, normal=normal)
                    for position in contours
                ],
            )
        ],
    )
    before = _snapshot(tmp_path)

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "skipped"
    assert result.warning_code == "insufficient_longitudinal_coverage"
    assert result.caudal_missing_mm == pytest.approx(expected_caudal)
    assert result.cranial_missing_mm == pytest.approx(expected_cranial)
    assert result.source_series_uid == series_uid
    assert _snapshot(tmp_path) == before


@pytest.mark.parametrize(
    "signed, modality",
    [(True, "CT"), (False, "MR")],
)
def test_in_plane_crop_preserves_pixel_representation(
    tmp_path: Path, signed: bool, modality: str
):
    series_uid, images = _write_image_series(
        tmp_path,
        count=6,
        rows=512,
        columns=512,
        pixel_spacing=(1.5, 2.0),
        signed=signed,
        modality=modality,
    )
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_pixels.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(2), _contour(3)])],
    )

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "cropped"
    assert (result.cropped_rows, result.cropped_columns) == (320, 320)
    ds = pydicom.dcmread(images[2][0])
    dtype = np.int16 if signed else np.uint16
    expected = np.arange(512 * 512, dtype=dtype).reshape(512, 512) + 2
    np.testing.assert_array_equal(ds.pixel_array, expected[96:-96, 96:-96])
    assert int(ds.PixelRepresentation) == int(signed)
    np.testing.assert_allclose(
        [float(value) for value in ds.ImagePositionPatient],
        [-320.0, -240.0, 2.0],
    )


def test_target_roi_outside_reduced_fov_fails_without_mutation(tmp_path: Path):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_outside.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[
            (
                "PTV+2cm_Ph",
                [_contour(3, offset=(-100, 0, 0)), _contour(5, offset=(-100, 0, 0))],
            )
        ],
    )
    before = _snapshot(tmp_path)

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    assert "reduced in-plane field of view" in result.error
    assert _snapshot(tmp_path) == before


def test_non_target_contour_outside_reduced_fov_is_allowed(tmp_path: Path):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_other_outside.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[
            ("PTV+2cm_Ph", [_contour(3), _contour(5)]),
            ("Organ", [_contour(4, offset=(-100, 0, 0))]),
        ],
    )

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "cropped"
    updated = pydicom.dcmread(rtstruct_path)
    outside_points = np.asarray(
        updated.ROIContourSequence[1].ContourSequence[0].ContourData,
        dtype=float,
    ).reshape((-1, 3))
    assert np.min(outside_points[:, 0]) < -90


def test_dimensions_must_exceed_twice_crop_amount(tmp_path: Path):
    series_uid, images = _write_image_series(
        tmp_path, count=4, rows=192, columns=256
    )
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_small.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(1), _contour(2)])],
    )
    before = _snapshot(tmp_path)

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    assert "must both exceed" in result.error
    assert _snapshot(tmp_path) == before


def test_inconsistent_in_plane_geometry_fails_without_mutation(tmp_path: Path):
    series_uid, images = _write_image_series(tmp_path, count=4)
    changed = pydicom.dcmread(images[-1][0])
    changed.PixelSpacing = ["1.1", "1.0"]
    pydicom.dcmwrite(images[-1][0], changed, enforce_file_format=True)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_inconsistent.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(1), _contour(2)])],
    )
    before = _snapshot(tmp_path)

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    assert "pixel spacing" in result.error
    assert _snapshot(tmp_path) == before


@pytest.mark.parametrize("unsupported_kind", ["compressed", "multiframe", "color"])
def test_unsupported_pixel_encoding_fails_without_mutation(
    tmp_path: Path, unsupported_kind: str
):
    series_uid, images = _write_image_series(tmp_path, count=4)
    changed = pydicom.dcmread(images[-1][0])
    if unsupported_kind == "compressed":
        changed.file_meta.TransferSyntaxUID = JPEGBaseline8Bit
        changed.PixelData = encapsulate([changed.PixelData])
        changed["PixelData"].is_undefined_length = True
    elif unsupported_kind == "multiframe":
        changed.NumberOfFrames = 2
    else:
        changed.SamplesPerPixel = 3
        changed.PhotometricInterpretation = "RGB"
    pydicom.dcmwrite(images[-1][0], changed, enforce_file_format=True)
    rtstruct_path = _write_rtstruct(
        tmp_path / f"RS_{unsupported_kind}.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(1), _contour(2)])],
    )
    before = _snapshot(tmp_path)

    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    expected = {
        "compressed": "Compressed",
        "multiframe": "Multiframe",
        "color": "single-sample",
    }[unsupported_kind]
    assert expected in result.error
    assert _snapshot(tmp_path) == before


def test_rtstruct_write_failure_does_not_delete_images(tmp_path: Path, monkeypatch):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_write_failure.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(3), _contour(5)])],
    )
    original_hash = _sha256(rtstruct_path)

    def fail_write(*args, **kwargs):
        raise OSError("simulated write failure")

    monkeypatch.setattr(crop_series.pydicom, "dcmwrite", fail_write)
    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    assert "simulated write failure" in result.error
    assert _sha256(rtstruct_path) == original_hash
    assert len(_series_files(tmp_path, series_uid)) == len(images)


def test_replacement_failure_rolls_back_all_files(tmp_path: Path, monkeypatch):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_delete_failure.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(3), _contour(5)])],
    )
    _write_reg(
        tmp_path / "REG_rollback.dcm",
        series_uid=series_uid,
        image_records=images,
    )
    before = _snapshot(tmp_path)
    real_replace = os.replace
    failed_once = False

    def fail_image_replace(source, destination):
        nonlocal failed_once
        if (
            not failed_once
            and ".crop_staging_" in str(source)
            and Path(destination).name.startswith("CT_")
        ):
            failed_once = True
            raise OSError("simulated replacement failure")
        return real_replace(source, destination)

    monkeypatch.setattr(crop_series.os, "replace", fail_image_replace)
    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    assert "simulated replacement failure" in result.error
    assert _snapshot(tmp_path) == before


def test_copy_and_crop_common_path_returns_skipped_result(monkeypatch):
    calls = []

    def fake_copy(*args, **kwargs):
        calls.append("copy")
        return "/tmp/RS_test.dcm"

    def fake_crop(*args, **kwargs):
        calls.append("crop")
        return crop_series.CropResult(status="skipped", warning="no unique ROI")

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)
    monkeypatch.setattr(crop_series, "crop_registered_series", fake_crop)

    result = crop_series.copy_structures_and_crop(
        "/tmp",
        "patient",
        "plan",
        object(),
        series_uid="1.2.3",
        base_series_uid="4.5.6",
    )

    assert result.status == "skipped"
    assert calls == ["copy", "crop"]


def test_copy_and_crop_common_path_raises_on_crop_failure(monkeypatch):
    monkeypatch.setattr(
        crop_series,
        "copy_structures",
        lambda *args, **kwargs: "/tmp/RS_test.dcm",
    )
    monkeypatch.setattr(
        crop_series,
        "crop_registered_series",
        lambda *args, **kwargs: crop_series.CropResult(
            status="failed", error="invalid geometry"
        ),
    )

    with pytest.raises(RuntimeError, match="invalid geometry"):
        crop_series.copy_structures_and_crop(
            "/tmp",
            "patient",
            "plan",
            object(),
            series_uid="1.2.3",
            base_series_uid="4.5.6",
        )


def test_copy_and_crop_failure_restores_exact_pre_copy_rtstruct(
    tmp_path: Path, monkeypatch
):
    series_uid = "1.2.3"
    rtstruct_path = tmp_path / f"RS_{series_uid}.dcm"
    original = b"exact original RTSTRUCT bytes\x00\xff"
    copied = b"copied contour content"
    rtstruct_path.write_bytes(original)

    def fake_copy(*args, **kwargs):
        rtstruct_path.write_bytes(copied)
        return str(rtstruct_path)

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)
    monkeypatch.setattr(
        crop_series,
        "crop_registered_series",
        lambda *args, **kwargs: crop_series.CropResult(
            status="failed", error="invalid geometry"
        ),
    )

    with pytest.raises(RuntimeError, match="invalid geometry"):
        crop_series.copy_structures_and_crop(
            str(tmp_path),
            "patient",
            "plan",
            object(),
            series_uid=series_uid,
            base_series_uid="4.5.6",
        )

    assert rtstruct_path.read_bytes() == original


def test_copy_and_crop_exception_restores_pre_copy_rtstruct(
    tmp_path: Path, monkeypatch
):
    series_uid = "1.2.3"
    rtstruct_path = tmp_path / f"RS_{series_uid}.dcm"
    original = b"original"
    rtstruct_path.write_bytes(original)

    def fake_copy(*args, **kwargs):
        rtstruct_path.write_bytes(b"copied")
        return str(rtstruct_path)

    def raise_during_crop(*args, **kwargs):
        raise OSError("unexpected crop exception")

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)
    monkeypatch.setattr(crop_series, "crop_registered_series", raise_during_crop)

    with pytest.raises(OSError, match="unexpected crop exception"):
        crop_series.copy_structures_and_crop(
            str(tmp_path),
            "patient",
            "plan",
            object(),
            series_uid=series_uid,
            base_series_uid="4.5.6",
        )

    assert rtstruct_path.read_bytes() == original


def test_copy_and_crop_failure_removes_new_rtstruct(tmp_path: Path, monkeypatch):
    series_uid = "1.2.3"
    rtstruct_path = tmp_path / f"RS_{series_uid}.dcm"

    def fake_copy(*args, **kwargs):
        rtstruct_path.write_bytes(b"new copied RTSTRUCT")
        return str(rtstruct_path)

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)
    monkeypatch.setattr(
        crop_series,
        "crop_registered_series",
        lambda *args, **kwargs: crop_series.CropResult(
            status="failed", error="invalid geometry"
        ),
    )

    with pytest.raises(RuntimeError, match="invalid geometry"):
        crop_series.copy_structures_and_crop(
            str(tmp_path),
            "patient",
            "plan",
            object(),
            series_uid=series_uid,
            base_series_uid="4.5.6",
        )

    assert not rtstruct_path.exists()


def test_copy_and_crop_skipped_keeps_copied_rtstruct(tmp_path: Path, monkeypatch):
    series_uid = "1.2.3"
    rtstruct_path = tmp_path / f"RS_{series_uid}.dcm"
    rtstruct_path.write_bytes(b"original")

    def fake_copy(*args, **kwargs):
        rtstruct_path.write_bytes(b"copied")
        return str(rtstruct_path)

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)
    monkeypatch.setattr(
        crop_series,
        "crop_registered_series",
        lambda *args, **kwargs: crop_series.CropResult(
            status="skipped", warning="no unique ROI"
        ),
    )

    result = crop_series.copy_structures_and_crop(
        str(tmp_path),
        "patient",
        "plan",
        object(),
        series_uid=series_uid,
        base_series_uid="4.5.6",
    )

    assert result.status == "skipped"
    assert rtstruct_path.read_bytes() == b"copied"


def test_copy_and_crop_success_keeps_cropped_rtstruct(tmp_path: Path, monkeypatch):
    series_uid = "1.2.3"
    rtstruct_path = tmp_path / f"RS_{series_uid}.dcm"
    rtstruct_path.write_bytes(b"original")

    def fake_copy(*args, **kwargs):
        rtstruct_path.write_bytes(b"copied")
        return str(rtstruct_path)

    def fake_crop(*args, **kwargs):
        rtstruct_path.write_bytes(b"cropped")
        return crop_series.CropResult(
            status="cropped",
            roi_name="PTV+2cm_Ph",
            retained_count=5,
            deleted_count=5,
            original_rows=256,
            original_columns=256,
            cropped_rows=64,
            cropped_columns=64,
        )

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)
    monkeypatch.setattr(crop_series, "crop_registered_series", fake_crop)

    result = crop_series.copy_structures_and_crop(
        str(tmp_path),
        "patient",
        "plan",
        object(),
        series_uid=series_uid,
        base_series_uid="4.5.6",
    )

    assert result.status == "cropped"
    assert rtstruct_path.read_bytes() == b"cropped"


@pytest.mark.parametrize("failure_kind", ["write", "staging", "replacement"])
def test_copy_and_crop_crop_transaction_failure_restores_pre_copy_snapshot(
    tmp_path: Path, monkeypatch, failure_kind: str
):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / f"RS_{series_uid}.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(3), _contour(5)])],
    )
    before = _snapshot(tmp_path)
    real_dcmwrite = crop_series.pydicom.dcmwrite
    real_replace = crop_series.os.replace

    def fake_copy(*args, **kwargs):
        dataset = pydicom.dcmread(rtstruct_path)
        dataset.StructureSetLabel = "COPIED"
        real_dcmwrite(rtstruct_path, dataset, write_like_original=False)

        if failure_kind == "write":
            def fail_staged_write(path, *write_args, **write_kwargs):
                if ".crop_staging_" in str(path):
                    raise OSError("simulated staged write failure")
                return real_dcmwrite(path, *write_args, **write_kwargs)

            monkeypatch.setattr(crop_series.pydicom, "dcmwrite", fail_staged_write)
        elif failure_kind == "staging":
            def fail_slice_staging(*stage_args, **stage_kwargs):
                raise OSError("simulated slice staging failure")

            monkeypatch.setattr(
                crop_series, "_stage_cropped_slices", fail_slice_staging
            )
        else:
            failed_once = False

            def fail_image_replace(source, destination):
                nonlocal failed_once
                if (
                    not failed_once
                    and ".crop_staging_" in str(source)
                    and Path(destination).name.startswith("CT_")
                ):
                    failed_once = True
                    raise OSError("simulated replacement failure")
                return real_replace(source, destination)

            monkeypatch.setattr(crop_series.os, "replace", fail_image_replace)
        return str(rtstruct_path)

    monkeypatch.setattr(crop_series, "copy_structures", fake_copy)

    with pytest.raises(RuntimeError, match="simulated"):
        crop_series.copy_structures_and_crop(
            str(tmp_path),
            "patient",
            "plan",
            object(),
            series_uid=series_uid,
            base_series_uid="4.5.6",
        )

    assert _snapshot(tmp_path) == before
