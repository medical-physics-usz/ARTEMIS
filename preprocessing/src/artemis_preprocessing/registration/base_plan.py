import json
import os
import socket
from contextlib import contextmanager
from pathlib import Path
from tempfile import TemporaryDirectory, mkdtemp

from pydicom import dcmread
from pydicom.errors import InvalidDicomError
from pydicom.pixels.utils import get_expected_length

from artemis_preprocessing.db.connector import DBHandler
from artemis_preprocessing.utils import get_datetime, require_env


_RECEIPT_NAME = ".artemis-base-plan.json"


@contextmanager
def _lock_cache(patient_directory: Path):
    """Serialize cache updates for a patient. The OS releases the lock on exit."""
    with (patient_directory / ".artemis-base-plan.lock").open("a+b") as lock:
        try:
            if os.name == "nt":
                import msvcrt

                lock.seek(0)
                msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl

                fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as exc:
            raise RuntimeError(f"Could not lock the base-plan cache: {exc}") from exc
        yield


def _validate_base_plan(
    directory: Path,
    patient_id: str,
    rtplan_label: str,
    rtplan_uid: str | None,
    *, transfer_counts: dict[str, int | None] | None = None,
) -> dict[str, dict[str, int]]:
    receipt = directory / _RECEIPT_NAME
    legacy_cache = transfer_counts is None and not receipt.is_file()
    plans = []
    structures = []
    imaging = {}
    seen = set()
    for path in directory.rglob("*.dcm"):
        if path.parent != directory:
            raise ValueError("Base-plan DICOM objects must be in the plan directory")
        try:
            ds = dcmread(path, stop_before_pixels=not legacy_cache)
        except (InvalidDicomError, EOFError) as exc:
            raise ValueError("Unreadable DICOM object in the base plan") from exc
        modality = getattr(ds, "Modality", None)
        if modality not in ("RTPLAN", "RTSTRUCT", "CT", "MR"):
            continue
        uid = getattr(ds, "SOPInstanceUID", None)
        if not uid or uid in seen or getattr(ds, "PatientID", None) != patient_id:
            raise ValueError("Base plan contains a mismatched patient, missing UID, or duplicate object")
        seen.add(uid)
        if modality == "RTPLAN":
            plans.append(ds)
        elif modality == "RTSTRUCT":
            structures.append(ds)
        else:
            series_uid = getattr(ds, "SeriesInstanceUID", None)
            if not series_uid:
                raise ValueError("Base-plan image is missing its Series Instance UID")
            if legacy_cache:
                pixels = getattr(ds, "PixelData", None)
                if not pixels:
                    raise ValueError("Cached base-plan image has no pixel data")
                try:
                    if not ds.file_meta.TransferSyntaxUID.is_encapsulated:
                        expected_length = get_expected_length(ds)
                    else:
                        expected_length = None
                except (AttributeError, TypeError, ValueError) as exc:
                    raise ValueError("Cached base-plan image has invalid pixel metadata") from exc
                if expected_length is not None and len(pixels) not in (
                    expected_length, expected_length + expected_length % 2,
                ):
                    raise ValueError("Cached base-plan image has incomplete pixel data")
            imaging.setdefault(series_uid, {})[uid] = path.stat().st_size

    if (
        len(plans) != 1
        or getattr(plans[0], "RTPlanLabel", None) != rtplan_label
        or (rtplan_uid and plans[0].SOPInstanceUID != rtplan_uid)
    ):
        raise ValueError("Base plan does not contain the requested RTPLAN")

    structure_refs = getattr(plans[0], "ReferencedStructureSetSequence", [])
    if (
        len(structures) != 1
        or len(structure_refs) != 1
        or structures[0].SOPInstanceUID != getattr(structure_refs[0], "ReferencedSOPInstanceUID", None)
    ):
        raise ValueError("Base plan does not contain the RTPLAN's referenced RTSTRUCT")

    expected_images = {}
    for frame in getattr(structures[0], "ReferencedFrameOfReferenceSequence", []):
        for study in getattr(frame, "RTReferencedStudySequence", []):
            for series in getattr(study, "RTReferencedSeriesSequence", []):
                series_uid = getattr(series, "SeriesInstanceUID", None)
                if not series_uid:
                    raise ValueError("RTSTRUCT is missing a referenced image series UID")
                referenced_uids = expected_images.setdefault(series_uid, set())
                for image in getattr(series, "ContourImageSequence", []):
                    uid = getattr(image, "ReferencedSOPInstanceUID", None)
                    if not uid:
                        raise ValueError("RTSTRUCT is missing a referenced image instance UID")
                    referenced_uids.add(uid)

    if not expected_images or imaging.keys() != expected_images.keys():
        raise ValueError("Base-plan image series do not match the RTSTRUCT references")
    for series_uid, expected in expected_images.items():
        if not expected.issubset(imaging[series_uid]):
            raise ValueError("Base plan is missing image instances referenced by the RTSTRUCT")

    received_uids = set().union(*imaging.values())
    for roi in getattr(structures[0], "ROIContourSequence", []):
        for contour in getattr(roi, "ContourSequence", []):
            for image in getattr(contour, "ContourImageSequence", []):
                if getattr(image, "ReferencedSOPInstanceUID", None) not in received_uids:
                    raise ValueError("Base plan is missing an image referenced by a contour")

    if transfer_counts is not None:
        if transfer_counts.keys() != imaging.keys():
            raise ValueError("Base-plan series do not match the completed transfers")
        for series_uid, count in transfer_counts.items():
            if count is None and not expected_images[series_uid]:
                raise ValueError("Base-plan image completeness could not be verified")
            if count is not None and count != len(imaging[series_uid]):
                raise ValueError("Base-plan image count does not match the completed transfer")
    elif not legacy_cache:
        if json.loads(receipt.read_text(encoding="utf-8")) != imaging:
            raise ValueError("Base-plan images do not match the saved transfer inventory")
    elif not all(expected_images.values()):
        raise ValueError("Existing base plan lacks an inventory or explicit image references")

    return imaging


def get_base_plan(patient_id, rtplan_label, rtplan_uid):
    """Reuse a complete base plan or publish a verified download at its cache path."""
    patient_directory = Path(require_env("BASEPLAN_DIR")) / patient_id
    moving_dir = patient_directory / rtplan_label
    patient_directory.mkdir(parents=True, exist_ok=True)

    with _lock_cache(patient_directory):
        if moving_dir.exists():
            try:
                _validate_base_plan(moving_dir, patient_id, rtplan_label, rtplan_uid)
            except ValueError as exc:
                print(f"{get_datetime()} Existing base plan failed validation: {exc}")
            else:
                print(f"{get_datetime()} Base plan exists")
                return

        print(f"{get_datetime()} Downloading the base plan")
        dbh = DBHandler(
            server_ip=require_env("SERVER_IP"),
            server_port=int(require_env("SERVER_PORT")),
            called_ae_title=require_env("SERVER_AE_TITLE"),
            calling_ae_title=socket.gethostname(),
            scp_port=int(require_env("SCP_PORT")),
        )
        with TemporaryDirectory(prefix=".base-plan-download-", dir=patient_directory) as workspace:
            staged = Path(workspace) / "download"
            transfer_counts = dbh.export_dicom(
                patient_id, rtplan_label, staged, to_export=("rtplan", "ct", "rtstruct"),
                rtplan_uid=rtplan_uid,
            )
            inventory = _validate_base_plan(
                staged, patient_id, rtplan_label, rtplan_uid, transfer_counts=transfer_counts,
            )
            (staged / _RECEIPT_NAME).write_text(json.dumps(inventory), encoding="utf-8")

            previous = None
            if moving_dir.exists():
                quarantine = Path(mkdtemp(prefix=".base-plan-incomplete-", dir=patient_directory))
                previous = moving_dir.rename(quarantine / moving_dir.name)
            try:
                staged.rename(moving_dir)
            except OSError:
                if previous is not None:
                    previous.rename(moving_dir)
                raise
