using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.StructureCreation
{
    class StructureHelpers
    {
        public static Structure MarginForAStructure(Structure structure)
        {
            double x1 = 0;
            double y1 = 0;
            double z1 = 10;
            double x2 = 0;
            double y2 = 0;
            double z2 = 10;
            AxisAlignedMargins margins = new AxisAlignedMargins(StructureMarginGeometry.Inner, x1, y1, z1, x2, y2, z2);
            structure.SegmentVolume = structure.AsymmetricMargin(margins);
            return structure;
        }


        public static Structure GetBodyStructure(StructureSet structureSet)
        {
            Structure body = structureSet.Structures.FirstOrDefault(x => x.Id == "BODY");
            if (body == null)
            {
                body = structureSet.Structures.FirstOrDefault(x => x.Id == "BODY2");
                if (body == null)
                {
                    throw new Exception("No BODY structure found! Acceptable names are BODY and BODY2.");
                }
            }
            return body;
        }
        public static Structure CreateCombinedPtv(StructureSet structureSet, List<Structure> ptvs, bool enableOverride, out List<string> warnings)
        {
            string structureDicomType = "CONTROL";
            string combinedPtvId = "PTV_All_Ph";
            Color combinedPtvColor = StructureSettings.orange;
            Structure combinedPtv = null;

            warnings = new List<string>();

            // If the structure exists and override is not enabled, just return the existing one
            if (!StructureIsWritable(structureSet, combinedPtvId, enableOverride))
            {
                warnings.Add($"Combined PTV structure {combinedPtvId} can not be created as it already exists!");
                return structureSet.Structures.FirstOrDefault(x => x.Id == combinedPtvId);
            }

            combinedPtv = structureSet.AddStructure(structureDicomType, combinedPtvId);
            combinedPtv.Color = combinedPtvColor;
            foreach (Structure ptv in ptvs)
            {
                combinedPtv.SegmentVolume = combinedPtv.SegmentVolume.Or(ptv);
            }

            return combinedPtv;
        }

        public static Structure ThresholdImage(StructureSet structureSet, int threshold, out List<string> warnings, string structureId = "thresholdResult", string structureDicomType = "CONTROL", Nullable<Color> structureColor = null, bool enableOverride = true)
        {
            string tempBodyId = "tempBODY";
            Structure thrStructure = null;
            StructureCode bodyStructureCode = null;
            warnings = new List<string>();

            try
            {
                // Save current body to a temporal structure
                Structure realBody = GetBodyStructure(structureSet);
                bodyStructureCode = realBody.StructureCode;
                while (structureSet.Structures.Any(x => x.Id == tempBodyId))
                {
                    tempBodyId += "Z";
                }
                Structure tempBody = structureSet.AddStructure(structureDicomType, tempBodyId);
                tempBody.SegmentVolume = realBody.SegmentVolume;
                structureSet.RemoveStructure(realBody);

                // Set thresholding parameters and threshold image
                SearchBodyParameters parameters = structureSet.GetDefaultSearchBodyParameters();
                parameters.FillAllCavities = false;
                parameters.Smoothing = false;
                parameters.LowerHUThreshold = threshold;
                Structure newBody = structureSet.CreateAndSearchBody(parameters);

                // Save threshold result to structure
                if (StructureHelpers.StructureIsWritable(structureSet, structureId, enableOverride))
                {
                    thrStructure = structureSet.AddStructure(structureDicomType, structureId);
                    thrStructure.SegmentVolume = newBody;
                    if (structureColor == null)
                    {
                        structureColor = StructureSettings.magenta;
                    }
                }
                else
                {
                    warnings.Add($"Could not create threshold structure, because structure {structureId} already exists!");
                    thrStructure = structureSet.Structures.FirstOrDefault(x => x.Id.Equals(structureId));
                }

                // Save body back to BODY structure and remove temporal strucrture
                newBody.SegmentVolume = tempBody;
                newBody.StructureCode = bodyStructureCode;
                structureSet.RemoveStructure(tempBody);
            }
            catch (Exception excp)
            {
                // Return body to structure set and delete the temporal structure even if there is an error
                Structure body = GetBodyStructure(structureSet);
                Structure tempBody = structureSet.Structures.FirstOrDefault(x => x.Id == tempBodyId);
                if (body != null && tempBody != null)
                {
                    body.SegmentVolume = tempBody;
                    body.StructureCode = bodyStructureCode;
                    structureSet.RemoveStructure(tempBody);
                }
                throw new Exception("ERROR with image thresholding:" + excp.Message);
            }

            return thrStructure;
        }

        // Checks if a structure with given name can be created.
        // If structure already exists and override is enable, the old structure is removed here
        public static bool StructureIsWritable(StructureSet structureSet, string structudeId, bool enableOverride)
        {
            bool structureExists = structureSet.Structures.Any(x => x.Id.Equals(structudeId, StringComparison.OrdinalIgnoreCase));
            if ((!enableOverride) && structureExists)
            {
                return false;
            }
            else if (enableOverride && structureExists)
            {
                if (structureSet.CanRemoveStructure(structureSet.Structures.FirstOrDefault(x => x.Id.Equals(structudeId, StringComparison.OrdinalIgnoreCase))))
                {
                    structureSet.RemoveStructure(structureSet.Structures.FirstOrDefault(x => x.Id.Equals(structudeId, StringComparison.OrdinalIgnoreCase)));
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

    }
}
