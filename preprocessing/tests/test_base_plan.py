from pathlib import Path
from types import SimpleNamespace

import pytest
from pydicom.dataset import Dataset, FileMetaDataset
from pydicom.uid import CTImageStorage, ExplicitVRLittleEndian, RTPlanStorage, RTStructureSetStorage

from artemis_preprocessing.db.connector import DBHandler
from artemis_preprocessing.registration import base_plan, core


PATIENT_ID = "DEMO"
PLAN_LABEL = "Example"
PLAN_UID = "1.2.3.1"
STRUCT_UID = "1.2.3.2"
SERIES_UID = "1.2.3.3"
IMAGE_UIDS = ("1.2.3.4", "1.2.3.5")


def write_dicom(directory, modality, sop_class, sop_uid, **attributes):
    ds = Dataset()
    ds.file_meta = FileMetaDataset()
    ds.file_meta.TransferSyntaxUID = ExplicitVRLittleEndian
    ds.SOPClassUID = sop_class
    ds.SOPInstanceUID = sop_uid
    ds.Modality = modality
    ds.PatientID = PATIENT_ID
    for key, value in attributes.items():
        setattr(ds, key, value)
    ds.save_as(directory / f"{sop_uid}.dcm", enforce_file_format=True)


def item(**attributes):
    ds = Dataset()
    for key, value in attributes.items():
        setattr(ds, key, value)
    return ds


def write_plan(directory, *, images=IMAGE_UIDS, explicit_images=True):
    directory.mkdir(parents=True, exist_ok=True)
    write_dicom(
        directory, "RTPLAN", RTPlanStorage, PLAN_UID, RTPlanLabel=PLAN_LABEL,
        ReferencedStructureSetSequence=[item(ReferencedSOPInstanceUID=STRUCT_UID)],
    )
    series = item(SeriesInstanceUID=SERIES_UID)
    if explicit_images:
        series.ContourImageSequence = [item(ReferencedSOPInstanceUID=uid) for uid in IMAGE_UIDS]
    write_dicom(
        directory, "RTSTRUCT", RTStructureSetStorage, STRUCT_UID,
        ReferencedFrameOfReferenceSequence=[item(RTReferencedStudySequence=[
            item(RTReferencedSeriesSequence=[series]),
        ])],
    )
    for index, uid in enumerate(images):
        write_dicom(
            directory, "CT", CTImageStorage, uid, SeriesInstanceUID=SERIES_UID,
            StudyInstanceUID="1.2.3.6", FrameOfReferenceUID="1.2.3.7",
            ImageOrientationPatient=[1, 0, 0, 0, 1, 0], ImagePositionPatient=[0, 0, index],
            SliceLocation=index * 2, PixelSpacing=[1, 1], Rows=2, Columns=2,
            PhotometricInterpretation="MONOCHROME2", SamplesPerPixel=1, PixelRepresentation=0,
            BitsAllocated=16, BitsStored=16, HighBit=15, PixelData=bytes(8),
        )


@pytest.fixture
def cache(tmp_path, monkeypatch):
    env = {
        "BASEPLAN_DIR": str(tmp_path), "SERVER_IP": "192.0.2.1", "SERVER_PORT": "104",
        "SERVER_AE_TITLE": "EXAMPLE", "SCP_PORT": "10400",
    }
    monkeypatch.setattr(base_plan, "require_env", env.__getitem__)
    return tmp_path / PATIENT_ID / PLAN_LABEL


def fetch_plan():
    base_plan.get_base_plan(PATIENT_ID, PLAN_LABEL, PLAN_UID)


def fake_export(monkeypatch, export):
    monkeypatch.setattr(base_plan, "DBHandler", lambda **_: SimpleNamespace(export_dicom=export))


def test_interrupted_download_does_not_publish_and_can_retry(cache, monkeypatch):
    abandoned = cache.parent / ".base-plan-download-abandoned" / "download"
    write_plan(abandoned, images=IMAGE_UIDS[:1])
    attempts = []

    def export(patient_id, plan_label, destination, **kwargs):
        assert (patient_id, plan_label, kwargs["rtplan_uid"]) == (PATIENT_ID, PLAN_LABEL, PLAN_UID)
        assert destination != cache
        assert not cache.exists()
        attempts.append(destination)
        if len(attempts) == 1:
            write_plan(destination, images=IMAGE_UIDS[:1])
            raise ConnectionError("Interrupted transfer")
        write_plan(destination)
        return {SERIES_UID: None}

    fake_export(monkeypatch, export)
    with pytest.raises(ConnectionError, match="Interrupted transfer"):
        fetch_plan()
    assert not cache.exists()
    assert not attempts[0].exists()

    fetch_plan()
    fetch_plan()
    assert len(attempts) == 2
    assert {path.stem for path in cache.glob("*.dcm")} == {PLAN_UID, STRUCT_UID, *IMAGE_UIDS}


def test_incomplete_cache_preserved_on_failed_download_and_replaced_on_retry(cache, monkeypatch):
    write_plan(cache, images=IMAGE_UIDS[:1])
    original = {path.name: path.read_bytes() for path in cache.iterdir()}
    attempts = []

    def export(*args, **kwargs):
        attempts.append(args[2])
        if len(attempts) == 1:
            write_plan(args[2], explicit_images=False, images=IMAGE_UIDS[:1])
        else:
            write_plan(args[2])
        return {SERIES_UID: 2}

    fake_export(monkeypatch, export)
    with pytest.raises(ValueError, match="image count does not match"):
        fetch_plan()
    assert {path.name: path.read_bytes() for path in cache.iterdir()} == original

    fetch_plan()
    assert (cache / f"{IMAGE_UIDS[1]}.dcm").is_file()
    quarantined, = cache.parent.glob(f".base-plan-incomplete-*/{PLAN_LABEL}")
    assert {path.name: path.read_bytes() for path in quarantined.iterdir()} == original


def test_publication_failure_restores_existing_cache(cache, monkeypatch):
    write_plan(cache, images=IMAGE_UIDS[:1])
    original = {path.name: path.read_bytes() for path in cache.iterdir()}

    def export(patient_id, plan_label, destination, **kwargs):
        write_plan(destination)
        return {SERIES_UID: 2}

    rename = Path.rename

    def fail_publication(path, target):
        if path.name == "download" and target == cache:
            raise OSError("Simulated publication failure")
        return rename(path, target)

    fake_export(monkeypatch, export)
    monkeypatch.setattr(Path, "rename", fail_publication)
    with pytest.raises(OSError, match="Simulated publication failure"):
        fetch_plan()
    assert {path.name: path.read_bytes() for path in cache.iterdir()} == original


def test_truncated_legacy_image_is_redownloaded(cache, monkeypatch):
    write_plan(cache)
    image = cache / f"{IMAGE_UIDS[1]}.dcm"
    original = image.read_bytes()
    image.write_bytes(original[:-2])

    def export(patient_id, plan_label, destination, **kwargs):
        write_plan(destination)
        return {SERIES_UID: 2}

    fake_export(monkeypatch, export)
    fetch_plan()
    assert image.read_bytes() == original


def test_reuses_complete_legacy_cache_and_preserves_images_used_for_validation(cache, monkeypatch):
    write_plan(cache)
    monkeypatch.setattr(base_plan, "DBHandler", lambda **_: pytest.fail("Complete cache was fetched again"))
    fetch_plan()

    _, filenames, _ = core.read_dicom_series(cache, series_uid=SERIES_UID, preserve_source=True)
    assert len(filenames) == 1
    assert filenames[0].endswith(f"{IMAGE_UIDS[0]}.dcm")
    assert (cache / f"{IMAGE_UIDS[1]}.dcm").is_file()
    fetch_plan()


def test_transfer_inventory_detects_missing_image_without_explicit_references(cache, monkeypatch):
    attempts = []

    def export(patient_id, plan_label, destination, **kwargs):
        attempts.append(destination)
        write_plan(destination, explicit_images=False)
        return {SERIES_UID: len(IMAGE_UIDS)}

    fake_export(monkeypatch, export)
    fetch_plan()
    fetch_plan()
    assert len(attempts) == 1

    (cache / f"{IMAGE_UIDS[1]}.dcm").unlink()
    fetch_plan()
    assert len(attempts) == 2
    assert (cache / f"{IMAGE_UIDS[1]}.dcm").is_file()


def test_active_cache_writer_blocks_a_concurrent_fetch(cache, monkeypatch):
    def export(patient_id, plan_label, destination, **kwargs):
        with pytest.raises(RuntimeError, match="Could not lock the base-plan cache"):
            fetch_plan()
        write_plan(destination)
        return {SERIES_UID: 2}

    fake_export(monkeypatch, export)
    fetch_plan()
    assert cache.is_dir()


@pytest.mark.parametrize(
    ("responses", "error"),
    [
        ([item(Status=0xB000)], "0xB000"),
        ([item(Status=0xA702)], "0xA702"),
        ([item(Status=0x0000, NumberOfFailedSuboperations=1)], "failed or warning"),
        ([item(Status=0xFF00), Dataset()], "without a final C-MOVE status"),
        ([item(Status=0xFF00)], "without a final C-MOVE status"),
    ],
)
def test_cmove_rejects_partial_or_failed_transfer(responses, error):
    handler = DBHandler.__new__(DBHandler)
    handler.calling_ae_title = "EXAMPLE"
    assoc = SimpleNamespace(send_c_move=lambda *_, **__: ((status, None) for status in responses))
    with pytest.raises(RuntimeError, match=error):
        handler._send_cmove(assoc, Dataset(), "CT series")


def test_cmove_returns_successful_store_count():
    handler = DBHandler.__new__(DBHandler)
    handler.calling_ae_title = "EXAMPLE"
    assoc = SimpleNamespace(send_c_move=lambda *_, **__: iter([
        (item(Status=0xFF00), None),
        (item(Status=0x0000, NumberOfCompletedSuboperations=2), None),
    ]))
    assert handler._send_cmove(assoc, Dataset(), "CT series") == 2
