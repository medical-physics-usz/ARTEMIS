using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using USZ_ARTEMIS.Core.Rules;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.Actions
{
    partial class Rules
    {
        public static void ApplyRules(
            PlanSetup targetPlan,
            PlanSetup rulesSourcePlan,
            IDictionary<string, bool> beforePlanCopy = null,
            IDictionary<string, bool> afterPlanCopy = null)
        {
            string rulesPath = RetrieveRulesFile(rulesSourcePlan);
            string path = ResolveRulesFilePath(rulesSourcePlan, rulesPath, "apply");
            if (path == null)
            {
                return;
            }

            var ruleSet = LoadRulesFromPath(path, targetPlan);
            StructureSet structureSet = targetPlan.StructureSet;
            var toDelete = structureSet.Structures
                .Where(s =>
                    s.Id.EndsWith("_ph", StringComparison.OrdinalIgnoreCase) &&
                    !(
                        (s.Id.StartsWith("ptv", StringComparison.OrdinalIgnoreCase)
                         && s.Id.EndsWith("+2cm_ph", StringComparison.OrdinalIgnoreCase))
                        || (s.Id?.StartsWith("OR", StringComparison.OrdinalIgnoreCase) == true)
                        || s.Id.Equals("highdensity_ph", StringComparison.OrdinalIgnoreCase)
                        || s.Id.Equals("highdensity_ph_inptv", StringComparison.OrdinalIgnoreCase)
                    ))
                .ToList();

            if (!PrepareRuleStructuresForHighResolution(
                    targetPlan,
                    toDelete,
                    beforePlanCopy,
                    afterPlanCopy))
            {
                return;
            }

            foreach (var structure in toDelete)
            {
                structureSet.RemoveStructure(structure);
            }

            var skippedDueToApproval = new List<string>();

            try
            {
                foreach (var rule in ruleSet.Rules)
                {
                    string outputId = rule.OutputStructure;
                    if (!string.IsNullOrEmpty(outputId))
                    {
                        var outStruct = targetPlan.StructureSet.Structures
                            .FirstOrDefault(s => s.Id.Equals(outputId, StringComparison.OrdinalIgnoreCase));

                        if (outStruct != null && outStruct.IsApproved)
                        {
                            skippedDueToApproval.Add($"{rule.Type} -> {outputId}");
                            continue;
                        }
                    }

                    switch (rule.Type)
                    {
                        case RuleType.Expansion:
                            if (rule.InputStructures.Count >= 1 && !string.IsNullOrEmpty(rule.OutputStructure))
                            {
                                string inId = rule.InputStructures[0];
                                string marginStr = (rule.MarginMm ?? 0).ToString(CultureInfo.InvariantCulture);
                                ApplyExpansion(targetPlan, inId, marginStr, rule.OutputStructure);
                            }
                            break;

                        case RuleType.MorphologicalOpening:
                            if (rule.InputStructures.Count >= 1 && !string.IsNullOrEmpty(rule.OutputStructure))
                            {
                                string inId = rule.InputStructures[0];
                                string marginStr = (rule.MarginMm ?? 0).ToString(CultureInfo.InvariantCulture);
                                ApplyMorphologicalOpening(targetPlan, inId, marginStr, rule.OutputStructure);
                            }
                            break;

                        case RuleType.AsymmetricExpansion:
                            if (rule.InputStructures.Count >= 1 &&
                                !string.IsNullOrEmpty(rule.OutputStructure) &&
                                rule.AsymmetricMarginsMm != null &&
                                rule.AsymmetricMarginsMm.Length == 6)
                            {
                                string inId = rule.InputStructures[0];
                                ApplyAsymmetricExpansion(targetPlan, inId, rule.OutputStructure, rule.AsymmetricMarginsMm);
                            }
                            break;

                        case RuleType.Subtraction:
                            if (rule.InputStructures.Count >= 2 && !string.IsNullOrEmpty(rule.OutputStructure))
                            {
                                ApplySubtractionMulti(targetPlan, rule.OutputStructure, rule.InputStructures);
                            }
                            break;

                        case RuleType.Addition:
                            if (rule.InputStructures.Count >= 2 && !string.IsNullOrEmpty(rule.OutputStructure))
                            {
                                ApplyAdditionMulti(targetPlan, rule.OutputStructure, rule.InputStructures);
                            }
                            break;

                        case RuleType.Intersection:
                            if (rule.InputStructures.Count >= 2 && !string.IsNullOrEmpty(rule.OutputStructure))
                            {
                                ApplyIntersectionMulti(targetPlan, rule.OutputStructure, rule.InputStructures);
                            }
                            break;

                        case RuleType.SbrtRing:
                            if (rule.InputStructures.Count == 2)
                            {
                                ApplySbrtRing(targetPlan, rule.InputStructures[0], rule.InputStructures[1]);
                            }
                            break;

                        case RuleType.RectalWall:
                            ApplyRectalWall(targetPlan);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("An error occurred: " + e.Message);
                return;
            }

            if (skippedDueToApproval.Count > 0)
            {
                MessageBox.Show(
                    "The following rules were skipped because the output structure is approved:\n\n" +
                    string.Join(Environment.NewLine, skippedDueToApproval) +
                    "\n\nIt's fine during the base plan preparation." +
                    "\nIf you see this message during the adaptation, it means that the structure set is approved. Unapprove it and try again.",
                    "Approved structures",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            MessageBox.Show("Rules were applied");
        }

        public static Structure EnsureHighResolution(Structure s)
        {
            if (s == null)
            {
                return null;
            }

            if (s.IsHighResolution)
            {
                return s;
            }

            if (s.IsApproved && RuleStructureResolutionPolicy.RequiresHighResolution(s.Id))
            {
                return s;
            }

            try
            {
                if (s.CanConvertToHighResolution())
                {
                    s.ConvertToHighResolution();

                    if (!s.IsHighResolution)
                    {
                        MessageBox.Show(
                            $"Structure '{s.Id}' was requested to convert to high resolution, but it still reports IsHighResolution = false.\n" +
                            "This can happen due to Eclipse/ESAPI restrictions in the current context.",
                            "High-resolution conversion not completed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    return s;
                }

                try
                {
                    s.ConvertToHighResolution();
                }
                catch (Exception ex2)
                {
                    MessageBox.Show(
                        $"Structure '{s.Id}' cannot be converted to high resolution.\n\n" +
                        $"DicomType: '{s.DicomType ?? "(null)"}'\n" +
                        $"IsEmpty: {s.IsEmpty}\n" +
                        $"IsApproved: {s.IsApproved}\n" +
                        $"CanConvertToHighResolution: {s.CanConvertToHighResolution()}\n\n" +
                        "ConvertToHighResolution() threw:\n" +
                        ex2.Message +
                        "\n\nCommon reasons:\n" +
                        "- The structure (or structure set) is approved/locked.\n" +
                        "- Structure is used for dose normalization and dose has been calculated.\n",
                        "High-resolution conversion not possible",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return s;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Exception while converting structure '{s.Id}' to high resolution:\n\n{ex.Message}",
                    "High-resolution conversion error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return s;
            }
        }

        public static void ApplyExpansion(PlanSetup SelectedPlan, string structureIn1Id, string margin_mm, string structureOutId)
        {
            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId, warnIfMissing: false, warnIfNotEmpty: false);
            structureOut = EnsureHighResolution(structureOut);

            Structure structureIn1 = FindStructureFromId(SelectedPlan, structureIn1Id, warnIfMissing: true, warnIfNotEmpty: true);
            structureIn1 = EnsureHighResolution(structureIn1);

            structureOut.SegmentVolume = structureIn1.SegmentVolume.Margin(Convert.ToDouble(margin_mm));
        }

        public static void ApplyMorphologicalOpening(PlanSetup SelectedPlan, string structureIn1Id, string margin_mm, string structureOutId)
        {
            ApplyExpansion(SelectedPlan, structureIn1Id, "-" + margin_mm, structureOutId);
            ApplyExpansion(SelectedPlan, structureOutId, margin_mm, structureOutId);
        }

        public static void ApplyAsymmetricExpansion(PlanSetup SelectedPlan, string structureInId, string structureOutId, double[] marginsMm)
        {
            if (marginsMm == null || marginsMm.Length != 6)
            {
                MessageBox.Show("Asymmetric expansion rule has invalid margins array.");
                return;
            }

            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId);
            structureOut = EnsureHighResolution(structureOut);

            Structure structureIn = FindStructureFromId(SelectedPlan, structureInId);
            structureIn = EnsureHighResolution(structureIn);

            var margins = new AxisAlignedMargins(
                StructureMarginGeometry.Outer,
                marginsMm[0], marginsMm[1],
                marginsMm[2], marginsMm[3],
                marginsMm[4], marginsMm[5]);

            structureOut.SegmentVolume = structureIn.AsymmetricMargin(margins);
        }

        public static void ApplySubtractionMulti(PlanSetup SelectedPlan, string structureOutId, IList<string> structureInIds)
        {
            if (structureInIds == null || structureInIds.Count == 0) return;

            bool outputIsAlsoInput = structureInIds.Any(id => id.Equals(structureOutId, StringComparison.OrdinalIgnoreCase));

            Structure structureOut = FindStructureFromId(
                SelectedPlan, structureOutId,
                warnIfMissing: outputIsAlsoInput,
                warnIfNotEmpty: false);
            structureOut = EnsureHighResolution(structureOut);

            Structure baseStr = FindStructureFromId(SelectedPlan, structureInIds[0], warnIfMissing: true, warnIfNotEmpty: true);
            baseStr = EnsureHighResolution(baseStr);

            var combinedVolume = baseStr.SegmentVolume;
            for (int i = 1; i < structureInIds.Count; i++)
            {
                Structure s = FindStructureFromId(SelectedPlan, structureInIds[i], warnIfMissing: true, warnIfNotEmpty: true);
                s = EnsureHighResolution(s);
                combinedVolume = combinedVolume.Sub(s);
            }

            structureOut.SegmentVolume = combinedVolume;
        }

        public static void ApplyAdditionMulti(PlanSetup SelectedPlan, string structureOutId, IList<string> structureInIds)
        {
            if (structureInIds == null || structureInIds.Count == 0) return;

            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId);
            structureOut = EnsureHighResolution(structureOut);

            Structure first = FindStructureFromId(SelectedPlan, structureInIds[0]);
            first = EnsureHighResolution(first);

            var combinedVolume = first.SegmentVolume;
            for (int i = 1; i < structureInIds.Count; i++)
            {
                Structure s = FindStructureFromId(SelectedPlan, structureInIds[i]);
                s = EnsureHighResolution(s);
                combinedVolume = combinedVolume.Or(s);
            }

            structureOut.SegmentVolume = combinedVolume;
        }

        public static void ApplyIntersectionMulti(PlanSetup SelectedPlan, string structureOutId, IList<string> structureInIds)
        {
            if (structureInIds == null || structureInIds.Count == 0) return;

            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId);
            structureOut = EnsureHighResolution(structureOut);

            Structure first = FindStructureFromId(SelectedPlan, structureInIds[0]);
            first = EnsureHighResolution(first);

            var combinedVolume = first.SegmentVolume;
            for (int i = 1; i < structureInIds.Count; i++)
            {
                Structure s = FindStructureFromId(SelectedPlan, structureInIds[i]);
                s = EnsureHighResolution(s);
                combinedVolume = combinedVolume.And(s);
            }

            structureOut.SegmentVolume = combinedVolume;
        }

        public static void ApplySbrtRing(PlanSetup SelectedPlan, string structureIn1Id, string structureIn2Id)
        {
            Structure ptv = FindStructureFromId(SelectedPlan, structureIn1Id);
            Structure itv = FindStructureFromId(SelectedPlan, structureIn2Id);
            Structure ptv_ph = FindStructureFromId(SelectedPlan, structureIn1Id + "_Ph");
            ptv_ph.SegmentVolume = ptv.SegmentVolume.Sub(itv.SegmentVolume.Margin(1));
        }

        public static void ApplyRectalWall(PlanSetup SelectedPlan)
        {
            Structure body = FindStructureFromId(SelectedPlan, "BODY");
            Structure bodyHR_Ph = FindStructureFromId(SelectedPlan, "BodyHR_Ph");
            Structure rectum = FindStructureFromId(SelectedPlan, "Rectum");
            Structure rectalWall = FindStructureFromId(SelectedPlan, "RectalWall_Ph");
            Structure rectalWallHelp = FindStructureFromId(SelectedPlan, "RectalWallHelp_Ph");

            rectum = EnsureHighResolution(rectum);
            bodyHR_Ph.SegmentVolume = body.SegmentVolume;
            bodyHR_Ph = EnsureHighResolution(bodyHR_Ph);

            rectalWallHelp.SegmentVolume = bodyHR_Ph.SegmentVolume.Sub(rectum);
            var margins = new AxisAlignedMargins(StructureMarginGeometry.Outer, 0, 6, 0, 0, 0, 0);
            rectalWallHelp.SegmentVolume = rectalWallHelp.AsymmetricMargin(margins);

            rectalWall.SegmentVolume = rectalWallHelp.SegmentVolume.And(rectum);
            rectalWallHelp.SegmentVolume = rectum.SegmentVolume.Sub(rectalWall);

            margins = new AxisAlignedMargins(StructureMarginGeometry.Outer, 30, 30, 0, 30, 0, 0);
            rectalWallHelp.SegmentVolume = rectalWallHelp.AsymmetricMargin(margins);
            rectalWall.SegmentVolume = rectalWall.SegmentVolume.Sub(rectalWallHelp);

            SelectedPlan.StructureSet.RemoveStructure(bodyHR_Ph);
            SelectedPlan.StructureSet.RemoveStructure(rectalWallHelp);
        }

        public static Structure FindStructureFromId(PlanSetup SelectedPlan, string structureID, bool warnIfMissing, bool warnIfNotEmpty)
        {
            var structureSet = SelectedPlan.StructureSet;
            var outStructure = structureSet.Structures
                .FirstOrDefault(s => s.Id.Equals(structureID, StringComparison.OrdinalIgnoreCase));

            bool isHelperPh = structureID.EndsWith("_Ph", StringComparison.OrdinalIgnoreCase);

            if (outStructure == null)
            {
                string structureType = "CONTROL";
                if (structureID.IndexOf("PTV", StringComparison.OrdinalIgnoreCase) >= 0) structureType = "PTV";
                else if (structureID.IndexOf("CTV", StringComparison.OrdinalIgnoreCase) >= 0) structureType = "CTV";
                else if (structureID.IndexOf("GTV", StringComparison.OrdinalIgnoreCase) >= 0) structureType = "GTV";

                outStructure = structureSet.AddStructure(structureType, structureID);

                if (outStructure == null)
                {
                    MessageBox.Show(
                        "Error! Structure '" + structureID +
                        "' was not found and could not be created in the structure set '" +
                        structureSet.Id + "'.\nPlease verify the rules.",
                        "Structure error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return outStructure;
            }

            return outStructure;
        }

        public static Structure FindStructureFromId(PlanSetup SelectedPlan, string structureID)
            => FindStructureFromId(SelectedPlan, structureID, warnIfMissing: true, warnIfNotEmpty: true);
    }
}
