using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using USZ_ARTEMIS.Core.Planning;
using USZ_ARTEMIS.DataQualification;
using USZ_ARTEMIS.StructureCreation;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private static readonly Regex RX_PLAN = new Regex(
            @"^[A-Z]{2}(?:[a-z]{3})?(?<mid>[^_]+)_",
            RegexOptions.Compiled);

        private static readonly Regex RX_PTV = new Regex(
            @"^PTV\d+_V(?<mid>[^_]+)_[^+]+\+2cm_Ph$",
            RegexOptions.Compiled);

        private static readonly Regex RX_PTV_FALLBACK = new Regex(
            @"^PTV(?<mid>.*)\+2cm_Ph$",
            RegexOptions.Compiled);

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

                    var editable = bNew.GetEditableParameters();
                    var cpsNew = editable.ControlPoints.ToList();
                    for (int i = 0; i < cpsNew.Count && i < cpsOld.Count; i++)
                    {
                        cpsNew[i].LeafPositions = cpsOld[i].LeafPositions;
                        cpsNew[i].JawPositions = cpsOld[i].JawPositions;
                    }
                    bNew.ApplyParameters(editable);
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

                        var editable = bNew.GetEditableParameters();
                        var cpsNew = editable.ControlPoints.ToList();
                        for (int i = 0; i < cpsNew.Count && i < cpsOld.Count; i++)
                        {
                            cpsNew[i].LeafPositions = cpsOld[i].LeafPositions;
                            cpsNew[i].JawPositions = cpsOld[i].JawPositions;
                        }
                        bNew.ApplyParameters(editable);
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

        private void PopulateStructureSets()
        {
            cbStructureSets.Items.Clear();

            var filteredSS = context.Patient.StructureSets
                .Where(ss =>
                {
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

        private static string ExtractTargetFromPlanId(string s)
        {
            var m = RX_PLAN.Match(s);
            if (!m.Success)
                throw new ArgumentException($"Plan ID '{s}' is not in the expected format");
            return m.Groups["mid"].Value;
        }

        private static string ExtractTargetFromPtvRing(string s)
        {
            var m = RX_PTV.Match(s);
            if (!m.Success)
                throw new ArgumentException($"PTV ring name '{s}' is not in the expected PTV...+2cm_Ph format");
            return m.Groups["mid"].Value;
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

            var copiedPlan = originalPlan.Course.CopyPlanSetup(originalPlan, newStructureSet, null);

            string fractionSuffix = cb_FractionLetter.Text;
            string newPlanId = originalPlan.Id + fractionSuffix;

            if (newPlanId.Length > 13)
            {
                newPlanId = originalPlan.Id.Remove(2, 3) + fractionSuffix;
                MessageBox.Show("Copied plan ID was shortend because a full plan ID would exceed the character limit.", "Plan Copy Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            copiedPlan.Id = newPlanId;
            copiedPlan.Name = originalPlan.Name + fractionSuffix;

            var targetMachineId = GetSelectedMachineId();
            if (!string.IsNullOrWhiteSpace(targetMachineId))
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
                        "Exact_IGRT_Couch_Top_medium",
                        true, out warnings);
                    if (warnings?.Count > 0)
                        warnings.ForEach(w => MessageBox.Show(w, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning));
                    else
                        MessageBox.Show("New couch structures created");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            Actions.Rules.ApplyRules(copiedPlan, originalPlan);

            var target = copiedPlan.StructureSet.Structures.FirstOrDefault(s => s.Id == copiedPlan.TargetVolumeID);
            if (target == null)
            {
                Structure targetVolume = copiedPlan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals(originalPlan.TargetVolumeID));
                StringBuilder errString = new StringBuilder();
                copiedPlan.SetTargetStructureIfNoDose(targetVolume, errString);
            }

            string targetPattern;
            try
            {
                targetPattern = ExtractTargetFromPlanId(copiedPlan.Id);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(
                    $"Invalid plan ID format: {ex.Message}",
                    "Structure selection error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var candidates = copiedPlan.StructureSet.Structures.Where(s => RX_PTV.IsMatch(s.Id)).ToList();
            var candidatesFallback = copiedPlan.StructureSet.Structures.Where(s => RX_PTV_FALLBACK.IsMatch(s.Id)).ToList();

            if (candidatesFallback.Count == 0)
            {
                MessageBox.Show("No PTV rings found in the structure set.", "Structure selection error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var matches = new List<Structure>();
            foreach (var s in candidates)
            {
                try
                {
                    if (ExtractTargetFromPtvRing(s.Id) == targetPattern)
                        matches.Add(s);
                }
                catch
                {
                }
            }

            if (matches.Count == 0)
            {
                if (candidatesFallback.Count == 1)
                {
                    var fallback = candidatesFallback[0];
                    MessageBox.Show(
                        $"Expected PTV ring for target V{targetPattern} but found none.\n" +
                        $"Falling back to the only available ring: {fallback.Id}",
                        "Structure selection warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    matches.Add(fallback);
                }
                else
                {
                    MessageBox.Show(
                        $"Expected PTV ring for target V{targetPattern} but found none.\n" +
                        $"Available rings: [{string.Join(", ", candidatesFallback.Select(s => s.Id))}]",
                        "Structure selection error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            if (matches.Count > 1)
            {
                MessageBox.Show(
                    $"Multiple PTV rings found for target V{targetPattern}: [{string.Join(", ", matches.Select(s => s.Id))}]",
                    "Structure selection error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var ptvPlus2cm = matches[0];
            bool requiresManualJawAndApertureAdjustment = false;
            PlanCopyApertureSafety apertureSafety;
            try
            {
                var ptvsOutsideRing = DataChecker.FindPtvsOutsideStructure(
                    copiedPlan.StructureSet,
                    ptvPlus2cm);
                apertureSafety = PlanCopyApertureSafety.FromContainmentCheck(
                    ptvPlus2cm.Id,
                    ptvsOutsideRing.Select(ptv => ptv.Id));
            }
            catch (Exception ex)
            {
                apertureSafety = PlanCopyApertureSafety.FromFailedCheck(ptvPlus2cm.Id, ex.Message);
            }

            if (apertureSafety.AllowAutomaticOptimization)
            {
                foreach (var beam in copiedPlan.Beams.Where(b => !b.Id.Contains("Setup")))
                {
                    beam.FitCollimatorToStructure(new FitToStructureMargins(0, 0, 0, 0), ptvPlus2cm, true, true, false);
                    if (beam.ControlPoints.Count > 2)
                    {
                        beam.FitArcOptimizationApertureToCollimatorJaws();
                    }
                }

                MessageBox.Show("Jaws adjusted to PTV+2cm_Ph and arc apertures set to jaws!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                requiresManualJawAndApertureAdjustment = true;
                MessageBox.Show(
                    apertureSafety.WarningMessage,
                    "PTV and +2 cm ring warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

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

            if (requiresManualJawAndApertureAdjustment)
            {
                MessageBox.Show(
                    "Plan copied. Automatic jaw and aperture optimization was skipped; manual adjustment is required.",
                    "Plan Copy Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show("Success!");
            }
        }

        private void cbMachine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
