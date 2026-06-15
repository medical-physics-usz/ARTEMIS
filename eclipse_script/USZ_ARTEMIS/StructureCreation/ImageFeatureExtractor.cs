using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.StructureCreation
{
    class ImageFeatureExtractor
    {
        private static Structure ThresholdCouchStructure(StructureSet structureSet)
        {
            string thrCouchId = "couchTest";
            string structureDicomType = "CONTROL";
            int HUThreshold = -600;
            double bodyMarginInMM = 10;

            // Use HU thresholding to find couch structures from image
            // Save found couch structure for debugging
            while (structureSet.Structures.Any(x => x.Id == thrCouchId))
            {
                thrCouchId += "Z";
            }
            Structure testCouch = StructureHelpers.ThresholdImage(structureSet, HUThreshold, out List<string> warnings, thrCouchId, structureDicomType);
            Structure body = StructureHelpers.GetBodyStructure(structureSet);
            testCouch.SegmentVolume = testCouch.Sub(body.Margin(bodyMarginInMM));

            return testCouch;
        }

        // Find correct position for couch
        // Couch interior bottom should be visible in the image
        public static double FindCouchShiftY(StructureSet structureSet, out List<string> warnings)
        {
            double additionalYShift = 1; // Change this to add or remove additional Y shift to correct the table position
            warnings = new List<string>();
            Structure couchInterior = structureSet.Structures.FirstOrDefault(x => x.Id == "CouchInterior");
            if (couchInterior == null || couchInterior.IsEmpty || couchInterior.MeshGeometry.Bounds.IsEmpty)
            {
                throw new Exception("Table shift in y-direction could not be extracted, as CouchInterior does not exist!");
            }
            double tableWidth = couchInterior.MeshGeometry.Bounds.SizeX;
            int tableWidthVox = (int)(tableWidth / structureSet.Image.YRes);
            Structure thrCouch = ThresholdCouchStructure(structureSet);
            Structure body = structureSet.Structures.FirstOrDefault(x => x.Id.Equals("BODY"));

            // Get a segment profile to tell which y-coordiantes in the middle line are inside sthresholded couch
            BitArray myLine = new BitArray(structureSet.Image.YSize / 2);
            VVector stop = body.CenterPoint;
            VVector start = stop;
            start.y += structureSet.Image.YRes * structureSet.Image.YSize / 2; // Start from bottom up
            var segmentProfile = thrCouch.GetSegmentProfile(start, stop, myLine).ToList();

            // Find the table bottom, that is the bottom most couch structure broad enough not to be a rounded table
            // minus signs used, because started from bottom
            double yShift = 0;
            foreach (var point in segmentProfile)
            {
                if (point.Value == false)
                {
                    continue;
                }

                //Get the segment profile of this point in x - direction
                BitArray myXLine = new BitArray(2 * tableWidthVox);
                VVector testPoint1 = point.Position;
                VVector testPoint2 = point.Position;
                testPoint1.x += tableWidth;
                testPoint2.x -= tableWidth;
                var xProfile = thrCouch.GetSegmentProfile(testPoint1, testPoint2, myXLine).ToList();
                int insideVoxels = xProfile.Count(x => x.Value == true);

                if (insideVoxels >= tableWidthVox / 3)
                {
                    yShift = point.Position.y - (couchInterior.MeshGeometry.Bounds.Y + couchInterior.MeshGeometry.Bounds.SizeY) + additionalYShift;
                    break;
                }
            }

            structureSet.RemoveStructure(thrCouch);

            return yShift;
        }

    }
}
