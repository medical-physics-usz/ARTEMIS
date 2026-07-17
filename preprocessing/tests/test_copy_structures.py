from pydicom.dataset import Dataset
from pydicom.sequence import Sequence

from artemis_preprocessing.dicom import copy_structures as copy_module


class IdentityTransform:
    def GetInverse(self):
        return self

    def TransformPoint(self, point):
        return point


def _make_rtstruct(roi_names):
    rtstruct = Dataset()
    rtstruct.StructureSetROISequence = Sequence()
    rtstruct.ROIContourSequence = Sequence()
    rtstruct.RTROIObservationsSequence = Sequence()

    for roi_number, roi_name in enumerate(roi_names, 1):
        roi = Dataset()
        roi.ROINumber = roi_number
        roi.ROIName = roi_name
        rtstruct.StructureSetROISequence.append(roi)

        contour = Dataset()
        contour.ContourData = ["0", "0", "0", "1", "1", "1"]
        roi_contour = Dataset()
        roi_contour.ReferencedROINumber = roi_number
        roi_contour.ContourSequence = Sequence([contour])
        rtstruct.ROIContourSequence.append(roi_contour)

        observation = Dataset()
        observation.ReferencedROINumber = roi_number
        rtstruct.RTROIObservationsSequence.append(observation)

    return rtstruct


def test_copy_structures_skips_ptvs_except_two_centimeter_helper(monkeypatch, tmp_path):
    base = _make_rtstruct(["PTV_1a", "PTVboost", "PTV+2cm_Ph", "CTV_1a"])
    target = _make_rtstruct(["Dummy_PH"])

    frame = Dataset()
    frame.FrameOfReferenceUID = "1.2.3"
    target.ReferencedFrameOfReferenceSequence = Sequence([frame])

    monkeypatch.setattr(copy_module, "read_base_rtstruct", lambda *args, **kwargs: base)
    monkeypatch.setattr(
        copy_module,
        "read_new_rtstruct",
        lambda *args, **kwargs: (target, "daily_rtstruct.dcm"),
    )
    monkeypatch.setattr(copy_module.pydicom, "dcmwrite", lambda *args, **kwargs: None)

    copy_module.copy_structures(
        str(tmp_path),
        "patient",
        "plan_1a",
        IdentityTransform(),
    )

    copied_names = [roi.ROIName for roi in target.StructureSetROISequence]
    assert copied_names == ["PTV+2cm_Ph", "CTV_1a"]

    copied_contours = {
        roi_contour.ReferencedROINumber: roi_contour.ContourSequence
        for roi_contour in target.ROIContourSequence
    }
    assert len(copied_contours[3]) == 1
    assert len(copied_contours[4]) == 1
