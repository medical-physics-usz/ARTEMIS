from __future__ import annotations

import hashlib
import math
import os
from pathlib import Path

import numpy as np
import pydicom
import pytest
from pydicom.dataset import Dataset, FileDataset, FileMetaDataset
from pydicom.sequence import Sequence
from pydicom.uid import (
    CTImageStorage,
    ExplicitVRLittleEndian,
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
) -> tuple[str, list[tuple[Path, str, np.ndarray]]]:
    series_uid = series_uid or generate_uid()
    row = np.asarray(iop[:3], dtype=float)
    column = np.asarray(iop[3:], dtype=float)
    normal = np.cross(row, column)
    records = []
    for index in range(count):
        path = directory / f"{prefix}_{index:03d}.dcm"
        sop_uid = generate_uid()
        ds = _file_dataset(path, CTImageStorage, sop_uid)
        ds.Modality = "CT"
        ds.SeriesInstanceUID = series_uid
        ds.StudyInstanceUID = generate_uid()
        ds.FrameOfReferenceUID = generate_uid()
        ds.ImageOrientationPatient = [f"{value:.10g}" for value in iop]
        position = normal * spacing * index
        ds.ImagePositionPatient = [f"{value:.10g}" for value in position]
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
) -> Dataset:
    normal = np.asarray(normal, dtype=float)
    row = np.asarray(row, dtype=float)
    column = np.asarray(column, dtype=float)
    center = normal * position
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


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


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
    assert len(_series_files(tmp_path, series_uid)) == 8
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
    expected_uids = {images[index][1] for index in range(1, 9)}
    assert retained_uids == expected_uids

    organ_contours = updated.ROIContourSequence[1].ContourSequence
    assert len(organ_contours) == 1
    for roi_contour in updated.ROIContourSequence:
        for contour in roi_contour.ContourSequence:
            assert len(contour.ContourImageSequence) == 1
            assert (
                str(contour.ContourImageSequence[0].ReferencedSOPInstanceUID)
                in retained_uids
            )


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
    assert {path.name for path in _series_files(tmp_path, series_uid)} == {
        images[index][0].name for index in range(1, 7)
    }


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


def test_slice_deletion_failure_returns_failed_status(tmp_path: Path, monkeypatch):
    series_uid, images = _write_image_series(tmp_path)
    rtstruct_path = _write_rtstruct(
        tmp_path / "RS_delete_failure.dcm",
        series_uid=series_uid,
        image_records=images,
        rois=[("PTV+2cm_Ph", [_contour(3), _contour(5)])],
    )
    real_remove = os.remove

    def fail_image_remove(path):
        if str(path).endswith("CT_000.dcm"):
            raise OSError("simulated deletion failure")
        return real_remove(path)

    monkeypatch.setattr(crop_series.os, "remove", fail_image_remove)
    result = crop_registered_series(str(tmp_path), series_uid, str(rtstruct_path))

    assert result.status == "failed"
    assert "simulated deletion failure" in result.error


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
