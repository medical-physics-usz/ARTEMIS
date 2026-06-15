using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using USZ_ARTEMIS.DataExtraction;
using USZ_ARTEMIS.DataQualification;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.StructureCreation
{
    class StructureCreator
    {

        public static Structure CreatePTVRing(StructureSet structureSet, List<Structure> ptvs, bool enableOverride, out List<string> warnings, Nullable<double> ringWidth = null, Nullable<double> ringGap = null)
        {
            warnings = new List<string>();
            List<string> newWarnings;
            Structure ptv = null;

            DataChecker.CheckNormalData(structureSet, ptvs);
            if (ptvs.Count == 1)
            {
                ptv = ptvs.FirstOrDefault();
            }
            else
            {
                ptv = StructureHelpers.CreateCombinedPtv(structureSet, ptvs, enableOverride, out newWarnings);
                warnings.AddRange(newWarnings);
            }
            Structure ring = CreatePTVRing(structureSet, ptv, enableOverride, out newWarnings, ringWidth, ringGap);
            warnings.AddRange(newWarnings);

            return ring;
        }

        public static Structure CreatePTVRing(StructureSet structureSet, Structure ptv, bool enableOverride, out List<string> warnings, Nullable<double> ringWidth = null, Nullable<double> ringGap = null)
        {
            string ringDicomType = "CONTROL";
            string ringId = StructureSettings.GetPtvRingId(ptv);
            Color ringColor = StructureSettings.white;
            warnings = new List<string>();

            // Set PTV ring thickness correctly
            double ringThicknessMm = StructureSettings.GetPtvRingDefaultWidth();
            if (ringWidth != null)
            {
                ringThicknessMm = (double)ringWidth;
            }
            if (ringThicknessMm <= 0)
            {
                throw new Exception("PTV ring width has to be a positive number or null!");
            }
            if (ringThicknessMm > 50)
            {
                throw new Exception("PTV ring width maximum is 50 mm!"); // This is maximum for structure margins
            }

            // Set PTV ring gap thickness correctly
            double ringGapMm = StructureSettings.GetPtvRingDefaultGap();
            if (ringGap != null)
            {
                ringGapMm = (double)ringGap;
            }
            if (ringGapMm < 0)
            {
                throw new Exception("PTV ring gap has to be a positive number or null!");
            }
            if (ringGapMm > 50)
            {
                throw new Exception("PTV ring gap maximum is 50 mm!"); // This is maximum for structure margins
            }

            var innerRingSegmentVolume = ptv.Margin(ringGapMm);
            var outerRingSegmentVolume = innerRingSegmentVolume.Margin(ringThicknessMm);

            // If the structure exists and override is not enabled, just return the existing one
            if (!StructureHelpers.StructureIsWritable(structureSet, ringId, enableOverride))
            {
                warnings.Add($"PTV ring structure {ringId} can not be created as it already exists!");
                return structureSet.Structures.FirstOrDefault(x => x.Id == ringId);
            }

            // Create PTV Ring structure
            var bodyStructure = StructureHelpers.GetBodyStructure(structureSet);
            var ring = structureSet.AddStructure(ringDicomType, ringId);
            //ring.SegmentVolume = outerRingSegmentVolume.Sub(innerRingSegmentVolume);
            ring.SegmentVolume = outerRingSegmentVolume;
            //ring.SegmentVolume = ring.And(bodyStructure); // Check that whole ring structure is inside body --> do not use this because PTV may be in high res and body not
            ring.Color = ringColor;

            // Crop the ring in longi by 10 mm
            AxisAlignedMargins margins = new AxisAlignedMargins(StructureMarginGeometry.Inner, 0, 0, 10, 0, 0, 10);
            ring.SegmentVolume = ring.AsymmetricMargin(margins);
            //ring = StructureHelpers.MarginForAStructure(ring);

            if (ring.IsEmpty)
            {
                structureSet.RemoveStructure(ring);
                throw new Exception($"PTV ring structure {ringId} is empty! Ring not created.");
            }

            return ring;
        }


        // Find Air in CT and creates a structure for that
        // Only returns new structures, not existing ones
        public static List<Structure> CreateAirStructures(StructureSet structureSet, bool enableOverride, out List<string> warnings)
        {
            string structureDicomType = "CONTROL";
            string airPTVId = "OR_Air_Ph";
            string strictAirId = "Air_Thr_Ph";
            int airThreshold = -100; // HU threshold of all air voxels, including ones with partial volume effects (old threshold = -150)
            int strictAirThreshold = -850; // HU threshold under which all voxels are air for sure and not for example lung tissue (old threshold = -150)
            double airMarginMm = 5; // Margin in which all air should be from strictly thresholded air voxels
            double HUToAssingToPTV = 0; // Air voxels inside PTV should be assigned to water.
            Color airStructColor = StructureSettings.white;
            string warningList = "";
            warnings = new List<string>();
            List<string> newWarnings = new List<string>();
            List<Structure> airStructures = new List<Structure>();

            // Threshold everything that is air for sure
            // Thresholding possible only with lower threshold value, so actually we get anti-air and not air
            Structure antiAir = StructureHelpers.ThresholdImage(structureSet, strictAirThreshold, out newWarnings, strictAirId, structureDicomType, enableOverride: enableOverride);
            warnings.AddRange(newWarnings);

            // Create structure for air inside the PTV
            if (StructureHelpers.StructureIsWritable(structureSet, airPTVId, enableOverride))
            {
                var airPTV = structureSet.AddStructure(structureDicomType, airPTVId);
                Structure body = StructureHelpers.GetBodyStructure(structureSet);
                airPTV.SegmentVolume = body.Sub(antiAir).Margin(airMarginMm);

                // Do thresholding again to find all possible air voxels
                structureSet.RemoveStructure(antiAir);
                antiAir = StructureHelpers.ThresholdImage(structureSet, airThreshold, out newWarnings, strictAirId, structureDicomType, enableOverride: enableOverride);
                warnings.AddRange(newWarnings);

                /*if (ptv.IsHighResolution)
                {
                    if (airPTV.CanConvertToHighResolution()) { airPTV.ConvertToHighResolution(); }
                    if (antiAir.CanConvertToHighResolution()) { antiAir.ConvertToHighResolution(); }
                }*/

                // Set the correct segment volume to the air inside ptv structure
                //airPTV.SegmentVolume = airPTV.Sub(antiAir).And(ptv);
                airPTV.SegmentVolume = airPTV.Sub(antiAir);

                if (airPTV.IsEmpty || airPTV.MeshGeometry.Bounds.IsEmpty)
                {
                    structureSet.RemoveStructure(airPTV);
                }

                if (airPTV.Volume < 0.2) // Small air volumes do not matter
                {
                    structureSet.RemoveStructure(airPTV);
                }
                airPTV.Color = airStructColor;
                airPTV.SetAssignedHU(HUToAssingToPTV);
                airStructures.Add(airPTV);
            }
            else
            {
                warningList += warningList.Length > 0 ? $", {airPTVId}" : airPTVId;
            }

            if (warningList.Length > 0)
            {
                warnings.Add($"Following air structures could not be created, as they already exist: {warningList}");
            }

            structureSet.RemoveStructure(antiAir);

            return airStructures;
        }

        // Creates high density structure for metal in body (HighDensity_Ph) and markers just outside of body (HighCheck_Ph)
        // Returns only the new high density structures, not existing ones
        public static List<Structure> CreateHighDensityStructures(StructureSet structureSet, bool enableOverride, out List<string> warnings, List<Structure> ptvs = null)
        {
            string structureDicomType = "CONTROL";
            string highDensInBodyId = "HighDensity_Ph";
            string markersId = "HighCheck_Ph";
            string highDensInBodyMarginId = "OR_Hip_Water_Ph";
            string highDensInPTVMarginId = "OR_Markers_Water_Ph";
            int HUThreshold = 1800; // Lowest that works at the moment // 2290; // HU value 2298 corresponds to density 3 g/cc
            double HUToAssing = 3567; // HU of titanium
            double bodyMarginMm = 5; // Margin (in mm) inside which markers are suppoused to be
            double markerMarginMm = 3; // Margin (in mm) with which marker structures are expanded
            Color highDensInColor = StructureSettings.magenta;
            Color highDensOutColor = StructureSettings.orange;

            string warningList = "";
            warnings = new List<string>();
            List<Structure> highDensStructures = new List<Structure>();
            List<string> newWarnings = new List<string>();

            // Threshold high densities
            Structure thresholdResult = StructureHelpers.ThresholdImage(structureSet, HUThreshold, out newWarnings, enableOverride: enableOverride);
            warnings.AddRange(newWarnings);

            // Separate markers from high density objects inside the body
            Structure body = StructureHelpers.GetBodyStructure(structureSet);
            Structure bodyHighRes = structureSet.AddStructure(structureDicomType, "Body_highres");
            bodyHighRes.SegmentVolume = body.SegmentVolume;

            // Convert to high resolution
            thresholdResult.ConvertToHighResolution();
            bodyHighRes.ConvertToHighResolution();

            SegmentVolume innerBody = bodyHighRes.Margin(-bodyMarginMm);
            SegmentVolume highDensInBody = thresholdResult.And(innerBody);
            SegmentVolume markers = thresholdResult.Sub(innerBody).And(bodyHighRes);
            markers = markers.Margin(markerMarginMm); // Expand the structures, otherwise often just one voxel

            // Remove markers from body and remove threshold structure
            // body.SegmentVolume = body.Sub(markers);
            structureSet.RemoveStructure(thresholdResult);
            structureSet.RemoveStructure(bodyHighRes);

            // Create structure for high density areas inside the body
            if (StructureHelpers.StructureIsWritable(structureSet, highDensInBodyId, enableOverride))
            {
                var highDensInStruct = structureSet.AddStructure(structureDicomType, highDensInBodyId);
                var highDensInMarginStruct = structureSet.AddStructure(structureDicomType, highDensInBodyMarginId);
                var highDensInPtvStruct = structureSet.AddStructure(structureDicomType, highDensInBodyId + "_inPTV");
                var highDensInPTVMarginStruct = structureSet.AddStructure(structureDicomType, highDensInPTVMarginId);
                AxisAlignedMargins margins = new AxisAlignedMargins(0, 10, 3, 3, 10, 3, 3);

                List<Structure> highResStructures = new List<Structure>() { highDensInStruct, highDensInMarginStruct, highDensInPtvStruct, highDensInPTVMarginStruct };
                foreach (Structure structure in highResStructures)
                {
                    structure.ConvertToHighResolution();
                }

                foreach (Structure ptv in ptvs)
                {
                    // Create structure for high density areas inside the body but outside ptv
                    highDensInStruct.SegmentVolume = highDensInBody.Sub(ptv);
                    highDensInStruct.Color = highDensInColor;
                    highDensInStruct.SetAssignedHU(HUToAssing);

                    //create 1cm margin for high density areas inside the body but outside ptv
                    highDensInMarginStruct.SegmentVolume = highDensInBody.Sub(ptv).Margin(10);
                    highDensInMarginStruct.Color = highDensInColor;
                    highDensInMarginStruct.SetAssignedHU(0);

                    highDensInPtvStruct.SegmentVolume = highDensInBody.And(ptv);
                    highDensInPtvStruct.SetAssignedHU(HUToAssing);

                    //create 1cm margin for high density areas inside the ptv
                    //highDensInPTVMarginStruct.SegmentVolume = highDensInBody.And(ptv).Margin(8);
                    highDensInPTVMarginStruct.SegmentVolume = highDensInBody.And(ptv).AsymmetricMargin(margins);
                    highDensInPTVMarginStruct.Color = highDensInColor;
                    highDensInPTVMarginStruct.SetAssignedHU(0);

                }
                //structureSet.RemoveStructure(highDensInPtvStruct);

                // Ask user to assign high material
                warnings.Add($"High density areas inside body");
                highDensStructures.Add(highDensInStruct);

                //if (highDensInStruct.IsEmpty){structureSet.RemoveStructure(highDensInStruct);}

                //if (highDensInMarginStruct.IsEmpty){structureSet.RemoveStructure(highDensInMarginStruct);}

                //if (highDensInPTVMarginStruct.IsEmpty){structureSet.RemoveStructure(highDensInPTVMarginStruct);}

            }
            else
            {
                warningList += warningList.Length > 0 ? $", {highDensInBodyId}" : highDensInBodyId;
            }

            // Create structure for high density areas outside the body
            if (StructureHelpers.StructureIsWritable(structureSet, markersId, enableOverride))
            {
                var markersStruct = structureSet.AddStructure(structureDicomType, markersId);
                markersStruct.SegmentVolume = markers;
                markersStruct.Color = highDensOutColor;
                if (markersStruct.IsEmpty)
                {
                    structureSet.RemoveStructure(markersStruct);
                }
                else
                {
                    warnings.Add($"Some high density areas removed from body, check structure {markersId}");
                    highDensStructures.Add(structureSet.Structures.FirstOrDefault(x => x.Id.Equals(markersId, StringComparison.OrdinalIgnoreCase)));
                }
            }
            else
            {
                warningList += warningList.Length > 0 ? $", {markersId}" : markersId;
            }

            if (warningList.Length > 0)
            {
                warnings.Add($"Following high density structures could not be created, as they already exist: {warningList}");
            }

            return highDensStructures;
        }

        // Positioning does not work perfectly, manual check needed
        public static void CreateCouchStructures(StructureSet structureSet, string couchModel, bool enableOverride, out List<string> warnings)
        {
            warnings = new List<string>();
            if (couchModel == null || couchModel.Length == 0)
            {
                throw new Exception("Please select a couch model before creating couch structures!");
            }

            // Create couch structures
            AddCouchWithBodyMargin(structureSet, couchModel, 5, enableOverride);

            // Move created couch structures to correct height
            List<string> newWarnings = new List<string>();
            var couchShiftY = ImageFeatureExtractor.FindCouchShiftY(structureSet, out newWarnings);
            warnings.AddRange(newWarnings);
            if (couchShiftY > 0 && couchShiftY <= 50) // 50 is maximum for margin method, too big shift probably a bug anyhow, do to visible undelaying support structures in CT
            {
                AddCouchWithBodyMargin(structureSet, couchModel, couchShiftY, enableOverride);
            }
            if (DataChecker.CouchInsideBody(structureSet))
            {
                warnings.Add($"WARNING: Couch inside BODY! Check that created couch structures are correctly positioned!");
            }
        }        


        public static void AddCouchWithBodyMargin(StructureSet structureSet, string couchModel, double bodyMargin, bool enableOverride)
        {
            string structureDicomType = "CONTROL";
            string tempBodyId = "tempBODY";

            PatientOrientation orientation = PatientOrientation.NoOrientation;
            RailPosition railPosition = RailPosition.In;
            var HUvalues = CouchDataExtractor.GetCouchHUValues(couchModel);

            IReadOnlyList<Structure> addedStructures;
            bool resized;
            string errorMsg;

            // Save current body to a temporal structure
            Structure realBody = StructureHelpers.GetBodyStructure(structureSet);
            while (structureSet.Structures.Any(x => x.Id == tempBodyId))
            {
                tempBodyId += "Z";
            }
            Structure tempBody = structureSet.AddStructure(structureDicomType, tempBodyId);
            tempBody.SegmentVolume = realBody;

            try
            {
                // Set margin to body, so that couch structures are set lower
                realBody.SegmentVolume = realBody.Margin(bodyMargin);

                // Remove existing couch and create new
                if (!structureSet.CanAddCouchStructures(out errorMsg))
                {
                    if (enableOverride)
                    {
                        structureSet.RemoveCouchStructures(out IReadOnlyList<string> removedStructureIds, out string error);
                    }
                    else
                    {
                        if (structureSet.Structures.Any(x => x.DicomType == "SUPPORT"))
                        {
                            throw new Exception("ERROR: Couch structures already exist!");
                        }
                        throw new Exception("ERROR: No couch structures created!");
                    }
                }
                structureSet.AddCouchStructures(couchModel, orientation, railPosition, railPosition, HUvalues[0], HUvalues[1], HUvalues[2], out addedStructures, out resized, out errorMsg);
                if (errorMsg != null && errorMsg.Length > 0)
                {
                    throw new Exception(errorMsg);
                }
                if (addedStructures == null)
                {
                    throw new Exception("ERROR: No couch structures created!");
                }

                // Put body volume back to normal and remove temporal body
                realBody.SegmentVolume = tempBody;
                structureSet.RemoveStructure(tempBody);
            }
            catch (Exception excp)
            {
                // Put body volume back to normal and remove temporal body
                realBody.SegmentVolume = tempBody;
                structureSet.RemoveStructure(tempBody);
                throw new Exception(excp.Message);
            }
        }

    }
}
