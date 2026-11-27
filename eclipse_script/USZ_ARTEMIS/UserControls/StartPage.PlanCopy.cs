using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.Core.Planning;
using USZ_ARTEMIS.DataExtraction;
using USZ_ARTEMIS.StructureCreation;
using USZ_ARTEMIS.Tools;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private const double PlanCopyPtvFitMarginMm = 5.0;
        private const string PlanCopyPtvFitStructureBaseId = "zPtvFit";
        private const string PlanCopyPtvResolutionStructureBaseId = "zPtvNorm";

        // Temporarily disabled; set to true to restore clinical-goal and mean-dose filtering.
        private static readonly bool FilterCopiedOptimizationObjectives = false;

        private Course GetSelectedCourse()
        {
            return context.Course;
        }

        private PlanSetup GetSelectedPlan()
        {
            return context.PlanSetup;
        }

        private string GetSelectedMachineId()
        {
            if (cbMachine?.SelectedItem is ComboBoxItem cbi && cbi.Content is string s1)
                return s1.Trim();
            return cbMachine?.Text?.Trim();
        }

        private static void SplitEnergyMode(string energyModeDisplayName, out string energyModeId, out string primaryFluenceModeId)
        {
            energyModeId = energyModeDisplayName?.Trim() ?? string.Empty;
            primaryFluenceModeId = null;
            if (string.IsNullOrWhiteSpace(energyModeId)) return;

            var parts = energyModeId.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                energyModeId = parts[0].Trim();
                primaryFluenceModeId = parts[1].Trim();
            }
        }

        private static string ResolveTechniqueId(Beam b)
        {
            try { return b.Technique?.Id; }
            catch { }
            return (b?.ControlPoints?.Count ?? 0) > 2 ? "ARC" : "STATIC";
        }

        private void RetargetPlanToMachine(ExternalPlanSetup plan, string machineId)
        {
            bool requireExactGeometry = DoseCheckLauncher.IsAccelaMachine(machineId);
            var originalBeams = plan.Beams.OrderBy(b => b.BeamNumber).ToList();
            var beamIdMap = new Dictionary<Beam, string>();
            var beamNameMap = new Dictionary<Beam, string>();

            foreach (var b in originalBeams)
            {
                SplitEnergyMode(b.EnergyModeDisplayName, out string energyId, out string primaryFluenceModeId);
                string techniqueId = ResolveTechniqueId(b);
                int doseRate = b.DoseRate;
                var mp = new ExternalBeamMachineParameters(machineId, energyId, doseRate, techniqueId, primaryFluenceModeId);
                if (b.MLC != null)
                    mp.MLCId = b.MLC.Id;

                var cpsOld = b.ControlPoints;
                bool isArc = (cpsOld.Count > 2) || (b.GantryDirection != GantryDirection.None);
                bool hasMlc = b.MLC != null;

                double col = cpsOld.First().CollimatorAngle;
                double couch = cpsOld.First().PatientSupportAngle;
                double gantryStart = cpsOld.First().GantryAngle;
                double gantryStop = cpsOld.Last().GantryAngle;
                var dir = b.GantryDirection;
                var iso = b.IsocenterPosition;

                Beam bNew;

                if (b.IsSetupField)
                {
                    var jaw0 = cpsOld.First().JawPositions;
                    if (hasMlc)
                    {
                        var leaf0 = cpsOld.First().LeafPositions;
                        bNew = plan.AddMLCSetupBeam(mp, leaf0, jaw0, col, gantryStart, couch, iso);
                    }
                    else
                    {
                        bNew = plan.AddSetupBeam(mp, jaw0, col, gantryStart, couch, iso);
                    }
                }
                else if (isArc)
                {
                    var mw = cpsOld.Select(cp => cp.MetersetWeight).ToList();
                    bNew = plan.AddVMATBeam(mp, mw, col, gantryStart, gantryStop, dir, couch, iso);

                    CopyControlPointGeometry(b, bNew, requireExactGeometry);
                }
                else
                {
                    var mw = cpsOld.Select(cp => cp.MetersetWeight).ToList();
                    var gantry = gantryStart;
                    var jaw0 = cpsOld.First().JawPositions;

                    if (hasMlc && mw.Count > 1)
                    {
                        if (string.Equals(techniqueId, "SMLC", StringComparison.OrdinalIgnoreCase))
                            bNew = plan.AddMultipleStaticSegmentBeam(mp, mw, col, gantry, couch, iso);
                        else
                            bNew = plan.AddSlidingWindowBeam(mp, mw, col, gantry, couch, iso);

                        CopyControlPointGeometry(b, bNew, requireExactGeometry);
                    }
                    else if (hasMlc)
                    {
                        bNew = plan.AddMLCBeam(mp, cpsOld.First().LeafPositions, jaw0, col, gantry, couch, iso);
                    }
                    else
                    {
                        bNew = plan.AddStaticBeam(mp, jaw0, col, gantry, couch, iso);
                    }
                }

                if (requireExactGeometry)
                    ValidateAccelaBeam(b, bNew, machineId);

                beamIdMap[bNew] = b.Id;
                beamNameMap[bNew] = b.Name;
            }

            foreach (var b in originalBeams)
                plan.RemoveBeam(b);

            foreach (var kv in beamIdMap)
            {
                if (!string.Equals(kv.Key.Id, kv.Value, StringComparison.Ordinal))
                {
                    kv.Key.Id = kv.Value;
                }
            }

            foreach (var kv in beamNameMap)
            {
                if (!string.Equals(kv.Key.Name, kv.Value, StringComparison.Ordinal))
                {
                    kv.Key.Name = kv.Value;
                }
            }
        }

        private static void CopyControlPointGeometry(Beam source, Beam destination, bool requireExactGeometry)
        {
            var editable = destination.GetEditableParameters();
            var sourcePoints = source.ControlPoints;
            var destinationPoints = editable.ControlPoints.ToList();
            if (requireExactGeometry && destinationPoints.Count != sourcePoints.Count)
                throw new InvalidOperationException($"Beam '{source.Id}' has an incompatible editable control-point count on Accela.");

            for (int i = 0; i < destinationPoints.Count && i < sourcePoints.Count; i++)
            {
                var sourceLeaves = sourcePoints[i].LeafPositions;
                if (requireExactGeometry)
                    ValidateAccelaLeafGeometry(source.Id, sourceLeaves, destinationPoints[i].LeafPositions);

                destinationPoints[i].LeafPositions = sourceLeaves;
                destinationPoints[i].JawPositions = sourcePoints[i].JawPositions;
            }
            destination.ApplyParameters(editable);
        }

        private static void ValidateAccelaBeam(Beam source, Beam destination, string machineId)
        {
            if (!string.Equals(destination.TreatmentUnit?.Id, machineId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.MLC?.Id, destination.MLC?.Id, StringComparison.Ordinal)
                || source.ControlPoints.Count != destination.ControlPoints.Count)
                throw new InvalidOperationException(
                    $"Beam '{source.Id}' has incompatible treatment unit, MLC, or control points on {machineId}.");

            for (int i = 0; i < source.ControlPoints.Count; i++)
                ValidateAccelaLeafGeometry(source.Id, source.ControlPoints[i].LeafPositions,
                    destination.ControlPoints[i].LeafPositions);
        }

        private static void ValidateAccelaLeafGeometry(string beamId, float[,] sourceLeaves, float[,] destinationLeaves)
        {
            if (sourceLeaves.GetLength(0) != destinationLeaves.GetLength(0)
                || sourceLeaves.GetLength(1) != destinationLeaves.GetLength(1))
                throw new InvalidOperationException($"Beam '{beamId}' has incompatible MLC leaf geometry on Accela.");
        }

        private bool TryRetargetAccelaPlan(PlanSetup copiedPlan, string machineId)
        {
            try
            {
                RetargetPlanToMachine((ExternalPlanSetup)copiedPlan, machineId);
                return true;
            }
            catch (Exception ex)
            {
                string cleanup = "The incomplete copy was removed.";
                try
                {
                    var course = copiedPlan.Course;
                    if (!course.CanRemovePlanSetup(copiedPlan))
                        throw new InvalidOperationException("Eclipse would not allow removal of the incomplete plan.");
                    course.RemovePlanSetup(copiedPlan);
                }
                catch (Exception cleanupError)
                {
                    cleanup = "Remove the incomplete plan or discard the current Eclipse modifications before saving.\n"
                        + cleanupError.Message;
                }

                MessageBox.Show($"Could not retarget the copied plan to {machineId}.\n\n{ex.Message}\n\n{cleanup}",
                    "Treatment Unit Change", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void PopulateStructureSets()
        {
            cbStructureSets.Items.Clear();
            StructureSet baseStructureSet = GetSelectedPlan()?.StructureSet;

            var filteredSS = context.Patient.StructureSets
                .Where(ss =>
                {
                    if (Actions.Rules.AreSameStructureSet(ss, baseStructureSet))
                        return false;

                    var img = ss.Image;
                    string imgId = img.Id;
                    if (imgId.Contains("kVCBCT") || imgId.Contains("QW") || imgId.Contains("QB"))
                        return false;

                    string ssId = ss.Id;
                    bool isCT = ssId.StartsWith("CT", StringComparison.OrdinalIgnoreCase)
                              || ssId.StartsWith("sCT", StringComparison.OrdinalIgnoreCase);
                    return isCT;
                })
                .OrderByDescending(ss => ss.HistoryDateTime);

            foreach (var ss in filteredSS)
            {
                cbStructureSets.Items.Add(ss.Id);
            }
        }

        private static Structure AddTemporaryPlanCopyStructure(
            StructureSet structureSet,
            string baseId,
            string purpose)
        {
            string candidate = baseId;
            while (structureSet.Structures.Any(structure =>
                structure.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                if (candidate.Length >= 16)
                {
                    throw new InvalidOperationException(
                        $"Could not create a uniquely named temporary {purpose} structure.");
                }

                candidate += "Z";
            }

            if (!structureSet.CanAddStructure("CONTROL", candidate))
            {
                throw new InvalidOperationException(
                    $"Eclipse cannot add the temporary {purpose} structure.");
            }

            return structureSet.AddStructure("CONTROL", candidate);
        }

        private static SegmentVolume UnionPtvSegmentVolumes(IReadOnlyList<Structure> ptvs)
        {
            var union = ptvs[0].SegmentVolume;
            for (int i = 1; i < ptvs.Count; i++)
            {
                union = union.Or(ptvs[i].SegmentVolume);
            }

            return union;
        }

        private static void ConvertTemporaryStructureToHighResolution(
            Structure structure,
            string purpose)
        {
            if (!structure.CanConvertToHighResolution())
            {
                throw new InvalidOperationException(
                    $"The temporary {purpose} structure cannot be converted to high resolution.");
            }

            structure.ConvertToHighResolution();
            if (!structure.IsHighResolution)
            {
                throw new InvalidOperationException(
                    $"The temporary {purpose} structure did not convert to high resolution.");
            }
        }

        private static void PopulatePtvFitStructure(
            Structure fitStructure,
            Structure resolutionStructure,
            IReadOnlyList<Structure> ptvs)
        {
            var standardResolutionPtvs = ptvs
                .Where(ptv => !ptv.IsHighResolution)
                .ToList();
            var highResolutionPtvs = ptvs
                .Where(ptv => ptv.IsHighResolution)
                .ToList();

            SegmentVolume union;
            if (highResolutionPtvs.Count == 0)
            {
                union = UnionPtvSegmentVolumes(standardResolutionPtvs);
            }
            else
            {
                ConvertTemporaryStructureToHighResolution(
                    fitStructure,
                    "PTV fitting");
                union = UnionPtvSegmentVolumes(highResolutionPtvs);

                if (standardResolutionPtvs.Count > 0)
                {
                    if (resolutionStructure == null)
                    {
                        throw new InvalidOperationException(
                            "The temporary PTV resolution-conversion structure is missing.");
                    }

                    resolutionStructure.SegmentVolume =
                        UnionPtvSegmentVolumes(standardResolutionPtvs);
                    if (resolutionStructure.IsEmpty)
                    {
                        throw new InvalidOperationException(
                            "The standard-resolution current PTV union is empty.");
                    }

                    ConvertTemporaryStructureToHighResolution(
                        resolutionStructure,
                        "PTV resolution-conversion");
                    union = union.Or(resolutionStructure.SegmentVolume);
                }
            }

            fitStructure.SegmentVolume = union.Margin(PlanCopyPtvFitMarginMm);
            if (fitStructure.IsEmpty)
            {
                throw new InvalidOperationException(
                    "The current PTV union plus 5 mm is empty.");
            }
        }

        private static string CombinePlanCopyFitErrors(
            string existingError,
            string additionalError)
        {
            if (string.IsNullOrWhiteSpace(existingError))
            {
                return additionalError;
            }

            return existingError + "\n\nCleanup error: " + additionalError;
        }

        private void BtnCopyPlan_Click(object sender, RoutedEventArgs e)
        {
            StructureSet newStructureSet = null;
            string selectedId = cbStructureSets.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedId))
            {
                newStructureSet = context.Patient.StructureSets.FirstOrDefault(ss => ss.Id == selectedId);
            }
            if (newStructureSet == null)
            {
                MessageBox.Show("No structure set selected", "Plan Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(cb_FractionLetter.Text))
            {
                MessageBox.Show("Please specify the fraction", "Plan Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var originalPlan = GetSelectedPlan();
            if (originalPlan == null)
            {
                MessageBox.Show("No base plan selected", "Plan Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (originalPlan.Id.EndsWith("aA") || originalPlan.Id.EndsWith("aB") ||
                originalPlan.Id.EndsWith("aC") || originalPlan.Id.EndsWith("aD") ||
                originalPlan.Id.EndsWith("aE"))
            {
                MessageBox.Show("Please select the base plan", "Plan Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (Actions.Rules.AreSameStructureSet(
                    newStructureSet,
                    originalPlan.StructureSet))
            {
                MessageBox.Show(
                    "The base plan's own structure set cannot be used as the copy destination. " +
                    "Choose a different structure set.",
                    "Plan Copy Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                PopulateStructureSets();
                return;
            }

            string targetMachineId = GetSelectedMachineId();
            bool isAccelaCopy = DoseCheckLauncher.IsAccelaMachine(targetMachineId);
            if (isAccelaCopy && (!AppPaths.AccelaPlanCopyEnabled || !(originalPlan is ExternalPlanSetup)))
            {
                MessageBox.Show(!AppPaths.AccelaPlanCopyEnabled
                        ? "Accela plan copying requires a commissioned configuration with AccelaPlanCopyEnabled set to true."
                        : "Select an external beam base plan for Accela plan copying.",
                    "Plan Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string materialTableWarning = Tools.PlanCopyMaterialTable.CheckCompatibility(
                newStructureSet.GetPhysicalMaterialTable,
                newStructureSet.CanUsePhysicalMaterialTable);

            var copiedPlan = originalPlan.Course.CopyPlanSetup(originalPlan, newStructureSet, null);

            // Retarget the new copy before applying changes to its shared destination structure set.
            if (isAccelaCopy && !TryRetargetAccelaPlan(copiedPlan, targetMachineId))
                return;

            if (string.IsNullOrEmpty(materialTableWarning))
            {
                materialTableWarning = Tools.PlanCopyMaterialTable.Assign(
                    copiedPlan.StructureSet.GetPhysicalMaterialTable,
                    copiedPlan.StructureSet.SetPhysicalMaterialTable);
            }

            if (!string.IsNullOrEmpty(materialTableWarning))
            {
                MessageBox.Show(
                    $"ARTEMIS could not automatically confirm physical material table " +
                    $"'{Tools.PlanCopyMaterialTable.TableName}' on the destination structure set.\n\n" +
                    "Plan copying will continue. Please assign or verify this table manually " +
                    "in Eclipse before dose calculation.\n\n" + materialTableWarning,
                    "Physical Material Table Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            string fractionSuffix = cb_FractionLetter.Text;
            PlanCopyNames copyNames = PlanCopyNames.Create(originalPlan.Id, originalPlan.Name, fractionSuffix);

            if (copyNames.WasShortened)
            {
                MessageBox.Show("Copied plan ID and name were shortened because a full plan ID would exceed the character limit.", "Plan Copy Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            copiedPlan.Id = copyNames.Id;
            copiedPlan.Name = copyNames.Name;

            if (!isAccelaCopy && !string.IsNullOrWhiteSpace(targetMachineId))
            {
                var eps = copiedPlan as ExternalPlanSetup;
                if (eps == null)
                {
                    MessageBox.Show("Copied plan is not an external photon plan; cannot set treatment unit.",
                        "Plan Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    RetargetPlanToMachine(eps, targetMachineId);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to retarget plan to machine '{targetMachineId}'.\n" +
                        $"Check that the machine supports the energy/MLC/technique of the base plan.\n\n{ex.Message}",
                        "Treatment Unit Change", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            var couchIds = new[] { "CouchSurface", "CouchInterior" };

            bool originalHasCouch =
                originalPlan.StructureSet.Structures.Any(s =>
                    !string.IsNullOrWhiteSpace(s.Id) &&
                    couchIds.Contains(s.Id.Trim(), StringComparer.OrdinalIgnoreCase));

            if (originalHasCouch)
            {
                try
                {
                    List<string> warnings;
                    StructureCreator.CreateCouchStructures(copiedPlan.StructureSet,
                        CouchDataExtractor.IgrtMediumModelName,
                        true, out warnings);
                    if (warnings?.Count > 0)
                        warnings.ForEach(w => MessageBox.Show(w, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning));
                    else
                        MessageBox.Show("New couch structures created");
                }
                catch (Exception ex)
                {
                    if (isAccelaCopy)
                    {
                        MessageBox.Show(
                            "Accela couch creation failed. Remove the incomplete plan or discard the current Eclipse modifications before saving.\n\n"
                            + ex.Message,
                            "Plan copy incomplete", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            var ruleApplicationResult = Actions.Rules.ApplyRules(copiedPlan, originalPlan);
            if (!ruleApplicationResult.Succeeded)
            {
                MessageBox.Show(
                    ruleApplicationResult.Cancelled
                        ? "Plan copy stopped because rule application was cancelled.\n\n" +
                          "The incomplete copied plan remains in the current unsaved Eclipse " +
                          "modifications. Remove it or discard the modifications before saving."
                        : "Plan copy stopped because the rules could not be applied safely.\n\n" +
                          "No target, aperture, or optimization-objective updates were performed " +
                          "after the failure. Remove the incomplete copied plan or discard the " +
                          "current Eclipse modifications before saving.",
                    "Plan copy incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var target = copiedPlan.StructureSet.Structures.FirstOrDefault(s => s.Id == copiedPlan.TargetVolumeID);
            if (target == null)
            {
                Structure targetVolume = copiedPlan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(originalPlan.TargetVolumeID));
                StringBuilder errString = new StringBuilder();
                copiedPlan.SetTargetStructureIfNoDose(targetVolume, errString);
            }

            var currentPtvs = copiedPlan.StructureSet.Structures
                .Where(structure => PlanCopyPtvSelection.IsEligible(
                    structure.Id,
                    structure.DicomType,
                    structure.IsEmpty))
                .OrderBy(structure => structure.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var treatmentBeams = copiedPlan.Beams
                .Where(beam => !beam.IsSetupField)
                .OrderBy(beam => beam.BeamNumber)
                .ToList();

            Structure fitStructure = null;
            var temporaryFitStructures = new List<Structure>();
            string fitError = null;
            try
            {
                if (currentPtvs.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No non-empty current-course PTV structures were found. " +
                        "Eligible structures must have DICOM type PTV and an ID starting with PTV.");
                }

                if (treatmentBeams.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No treatment beams were found in the copied plan.");
                }

                fitStructure = AddTemporaryPlanCopyStructure(
                    copiedPlan.StructureSet,
                    PlanCopyPtvFitStructureBaseId,
                    "PTV fitting");
                temporaryFitStructures.Add(fitStructure);

                Structure resolutionStructure = null;
                if (currentPtvs.Any(ptv => ptv.IsHighResolution) &&
                    currentPtvs.Any(ptv => !ptv.IsHighResolution))
                {
                    resolutionStructure = AddTemporaryPlanCopyStructure(
                        copiedPlan.StructureSet,
                        PlanCopyPtvResolutionStructureBaseId,
                        "PTV resolution-conversion");
                    temporaryFitStructures.Add(resolutionStructure);
                }

                PopulatePtvFitStructure(
                    fitStructure,
                    resolutionStructure,
                    currentPtvs);

                foreach (var beam in treatmentBeams)
                {
                    beam.FitCollimatorToStructure(
                        new FitToStructureMargins(0, 0, 0, 0),
                        fitStructure,
                        true,
                        true,
                        false);
                    if (beam.ControlPoints.Count > 2)
                    {
                        beam.FitArcOptimizationApertureToCollimatorJaws();
                    }
                }
            }
            catch (Exception ex)
            {
                fitError = ex.Message;
            }
            finally
            {
                for (int i = temporaryFitStructures.Count - 1; i >= 0; i--)
                {
                    Structure temporaryStructure = temporaryFitStructures[i];
                    try
                    {
                        if (!copiedPlan.StructureSet.CanRemoveStructure(temporaryStructure))
                        {
                            throw new InvalidOperationException(
                                $"Eclipse cannot remove temporary structure '{temporaryStructure.Id}'.");
                        }

                        copiedPlan.StructureSet.RemoveStructure(temporaryStructure);
                    }
                    catch (Exception ex)
                    {
                        fitError = CombinePlanCopyFitErrors(fitError, ex.Message);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(fitError))
            {
                MessageBox.Show(
                    "Plan copy stopped because ARTEMIS could not safely fit the jaws and apertures " +
                    "to the current PTV union plus 5 mm.\n\n" +
                    "No optimization objectives were copied after this failure. The incomplete " +
                    "copied plan may contain partial beam changes and remains in the current unsaved " +
                    "Eclipse modifications. Remove it or discard the modifications before saving.\n\n" +
                    $"Details: {fitError}",
                    "Plan copy incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(
                "Jaws adjusted to the current PTV union plus 5 mm and arc apertures set to the jaws.",
                "Done",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            var structureLookup = copiedPlan.StructureSet.Structures
                .Where(structure => !structure.IsEmpty && !string.IsNullOrWhiteSpace(structure.Id))
                .ToDictionary(structure => structure.Id.Trim(), StringComparer.OrdinalIgnoreCase);

            var originalBody = originalPlan.StructureSet.Structures.FirstOrDefault(s => s.Id == "BODY");
            var copiedBody = copiedPlan.StructureSet.Structures.FirstOrDefault(s => s.Id == "BODY");
            // MessageBox.Show($"Original BODY structure code: {originalBody?.StructureCode}");
            // MessageBox.Show($"Copied BODY structure code: {copiedBody?.StructureCode}");
            if (originalBody?.StructureCode != null && copiedBody?.StructureCode == null)
            {
                copiedBody.StructureCode = originalBody.StructureCode;
                // MessageBox.Show($"Copied BODY structure code updated to: {copiedBody.StructureCode}");
            }

            var objectiveFilter = FilterCopiedOptimizationObjectives
                ? new PlanCopyObjectiveFilter(
                    originalPlan.GetClinicalGoals().Select(goal => goal.StructureId))
                : null;

            foreach (var objective in originalPlan.OptimizationSetup.Objectives)
            {
                var originalStructure = objective.Structure;
                if (string.IsNullOrWhiteSpace(originalStructure?.Id))
                {
                    MessageBox.Show(
                        "Skipped an objective because its associated structure is undefined or has an empty ID.",
                        "Objective Copy Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    continue;
                }

                var structureKey = originalStructure.Id.Trim();
                if (objectiveFilter != null && !objectiveFilter.ShouldCopy(
                    structureKey,
                    objective is OptimizationMeanDoseObjective))
                {
                    continue;
                }

                if (!structureLookup.TryGetValue(structureKey, out var copiedStructure))
                {
                    MessageBox.Show(
                        $"Objective for structure '{structureKey}' was not copied because the structure was not found in the copied originalPlan.",
                        "Objective Copy Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    continue;
                }

                switch (objective)
                {
                    case OptimizationPointObjective pointObjective:
                        copiedPlan.OptimizationSetup.AddPointObjective(
                            copiedStructure,
                            pointObjective.Operator,
                            pointObjective.Dose,
                            pointObjective.Volume,
                            pointObjective.Priority);
                        break;

                    case OptimizationMeanDoseObjective meanDoseObjective:
                        copiedPlan.OptimizationSetup.AddMeanDoseObjective(
                            copiedStructure,
                            meanDoseObjective.Dose,
                            meanDoseObjective.Priority);
                        break;
                }
            }

            var ntoOriginal = originalPlan.OptimizationSetup.Parameters.OfType<OptimizationNormalTissueParameter>().FirstOrDefault();
            try
            {
                copiedPlan.OptimizationSetup.AddNormalTissueObjective(
                    ntoOriginal.Priority,
                    ntoOriginal.DistanceFromTargetBorderInMM,
                    ntoOriginal.StartDosePercentage,
                    ntoOriginal.EndDosePercentage,
                    ntoOriginal.FallOff);

                MessageBox.Show("Plan optimization objectives copied!");
            }
            catch (Exception)
            {
                MessageBox.Show("NTO objectives not set. Please set them manually to: 150, 0.5 mm, 100%, 30%, 0.3 fall-of.", "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            var structureReminders = Tools.PlanCopyStructureReminders.Create(
                copiedPlan.StructureSet.Structures.Select(structure => structure.Id));
            if (!string.IsNullOrEmpty(structureReminders.AvoidEntryDoseReminder))
            {
                MessageBox.Show(
                    structureReminders.AvoidEntryDoseReminder,
                    "Plan Optimization Reminder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            if (!string.IsNullOrEmpty(structureReminders.MaterialOverrideReminder))
            {
                MessageBox.Show(
                    structureReminders.MaterialOverrideReminder,
                    "Manual Material Override Verification Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            MessageBox.Show("Success!");
        }

        private void cbStructureSets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StructureSet selectedSS = null;
            string selectedId = cbStructureSets.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedId))
            {
                selectedSS = context.Patient.StructureSets.FirstOrDefault(ss => ss.Id == selectedId);
                DateTime lastMod = selectedSS.HistoryDateTime;
                DateTime now = DateTime.Now;

                bool modifiedToday = lastMod.Date == now.Date;
                if (!modifiedToday)
                {
                    MessageBox.Show("The Structure Set that you selected hasn't been modified today! Make sure that you are using the right one!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
