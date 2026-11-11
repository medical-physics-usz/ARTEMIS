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
        public static RuleApplicationResult ApplyRules(PlanSetup targetPlan, PlanSetup rulesSourcePlan)
        {
            if (!ReferenceEquals(targetPlan, rulesSourcePlan) &&
                AreSameStructureSet(targetPlan?.StructureSet, rulesSourcePlan?.StructureSet))
            {
                MessageBox.Show(
                    "The copied plan and base plan share the same structure set. " +
                    "Applying adaptation rules would modify the base plan's structures.\n\n" +
                    "Choose a different destination structure set and copy the plan again.",
                    "Plan copy structure set",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return RuleApplicationResult.Failure();
            }

            string rulesPath = RetrieveRulesFile(rulesSourcePlan);
            string path = ResolveRulesFilePath(rulesSourcePlan, rulesPath, "apply");
            if (path == null)
            {
                return RuleApplicationResult.Cancel();
            }

            var ruleSet = LoadRulesFromPath(path, targetPlan);
            StructureSet structureSet = targetPlan.StructureSet;
            var ctv1Rules = ruleSet.Rules.Where(rule => rule.Type == RuleType.Ctv1).ToList();
            // CTV1 outputs are updated in place to preserve their structure metadata.
            var ctv1OutputIds = new HashSet<string>(
                ctv1Rules.SelectMany(rule => Ctv1Rule.GetOutputIds(rule.OutputStructure?.Trim())),
                StringComparer.OrdinalIgnoreCase);
            var toDelete = structureSet.Structures
                .Where(s =>
                    !string.IsNullOrWhiteSpace(s.Id) &&
                    s.Id.EndsWith("_ph", StringComparison.OrdinalIgnoreCase) &&
                    !ctv1OutputIds.Contains(s.Id.Trim()) &&
                    !(
                        (s.Id.StartsWith("ptv", StringComparison.OrdinalIgnoreCase)
                         && s.Id.EndsWith("+2cm_ph", StringComparison.OrdinalIgnoreCase))
                        || (s.Id?.StartsWith("OR", StringComparison.OrdinalIgnoreCase) == true)
                        || s.Id.Equals("highdensity_ph", StringComparison.OrdinalIgnoreCase)
                        || s.Id.Equals("highdensity_ph_inptv", StringComparison.OrdinalIgnoreCase)
                    ))
                .ToList();

            RuleMetadataPreparationPlan metadataPreparationPlan = null;
            bool copyPlanRuleApplication = !ReferenceEquals(targetPlan, rulesSourcePlan);
            if (copyPlanRuleApplication)
            {
                try
                {
                    metadataPreparationPlan = BuildMetadataPreparationPlan(
                        ruleSet,
                        targetPlan,
                        rulesSourcePlan,
                        toDelete);
                }
                catch (Exception e)
                {
                    MessageBox.Show(
                        "Structure metadata preflight could not be completed:\n\n" +
                        e.Message +
                        "\n\nNo rules were applied.",
                        "Plan copy structure metadata",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return RuleApplicationResult.Failure();
                }

                if (!metadataPreparationPlan.CanApply)
                {
                    MessageBox.Show(
                        FormatMetadataPreflightFailure(metadataPreparationPlan),
                        "Plan copy structure metadata",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return RuleApplicationResult.Failure();
                }

                string missingInputConfirmation =
                    FormatMissingInputCreationConfirmation(metadataPreparationPlan);
                if (missingInputConfirmation != null &&
                    MessageBox.Show(
                        missingInputConfirmation,
                        "Plan copy missing structures",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return RuleApplicationResult.Cancel();
                }
            }

            var skippedDueToApproval = new List<string>();

            try
            {
                foreach (var rule in ctv1Rules)
                {
                    string error = Ctv1Rule.GetTargetIdError(rule.OutputStructure);
                    if (error.Length > 0)
                    {
                        throw new InvalidOperationException(error);
                    }
                }

                foreach (var structure in toDelete)
                {
                    structureSet.RemoveStructure(structure);
                }

                if (metadataPreparationPlan != null)
                {
                    PrepareRuleStructures(
                        targetPlan.StructureSet,
                        rulesSourcePlan.StructureSet,
                        metadataPreparationPlan);
                }

                foreach (var rule in ruleSet.Rules)
                {
                    string outputId = rule.OutputStructure;
                    if (rule.Type != RuleType.Ctv1 && !string.IsNullOrEmpty(outputId))
                    {
                        var outStruct = FindExistingStructure(
                            targetPlan.StructureSet,
                            outputId);

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

                        case RuleType.Ctv1:
                            ApplyCtv1(targetPlan, outputId.Trim());
                            break;
                    }
                }

                if (metadataPreparationPlan != null)
                {
                    VerifyRuleOutputMetadata(
                        ruleSet,
                        targetPlan.StructureSet,
                        rulesSourcePlan.StructureSet);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(
                    "Rule application stopped before completion:\n\n" +
                    e.Message +
                    "\n\nDo not save the incomplete plan. Correct the problem or " +
                    "discard the current Eclipse modifications before trying again.",
                    "Rule application failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return RuleApplicationResult.Failure();
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

            string completionMessage = "Rules were applied";
            if (metadataPreparationPlan?.Warnings.Count > 0)
            {
                completionMessage +=
                    "\n\nMetadata warnings:\n- " +
                    string.Join("\n- ", metadataPreparationPlan.Warnings);
            }

            MessageBox.Show(completionMessage);
            return RuleApplicationResult.Success();
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
            Structure structureIn1 = FindRequiredInputStructure(SelectedPlan, structureIn1Id);
            RejectEmptySafetyCriticalMarginInput(structureIn1);
            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId, warnIfMissing: false, warnIfNotEmpty: false);
            if (structureIn1.IsEmpty)
            {
                ClearRuleOutput(SelectedPlan, structureOut);
                return;
            }

            structureOut = EnsureHighResolution(structureOut);
            structureIn1 = EnsureHighResolution(structureIn1);

            structureOut.SegmentVolume = structureIn1.SegmentVolume.Margin(Convert.ToDouble(margin_mm));
        }

        public static void ApplyAsymmetricExpansion(PlanSetup SelectedPlan, string structureInId, string structureOutId, double[] marginsMm)
        {
            if (marginsMm == null || marginsMm.Length != 6)
            {
                MessageBox.Show("Asymmetric expansion rule has invalid margins array.");
                return;
            }

            Structure structureIn = FindRequiredInputStructure(SelectedPlan, structureInId);
            RejectEmptySafetyCriticalMarginInput(structureIn);
            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId);
            if (structureIn.IsEmpty)
            {
                ClearRuleOutput(SelectedPlan, structureOut);
                return;
            }

            structureOut = EnsureHighResolution(structureOut);
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
            var inputStructures = structureInIds
                .Select(id => FindRequiredInputStructure(SelectedPlan, id))
                .ToList();
            Structure baseStr = inputStructures[0];
            Structure structureOut = FindStructureFromId(
                SelectedPlan, structureOutId,
                warnIfMissing: outputIsAlsoInput,
                warnIfNotEmpty: false);
            if (baseStr.IsEmpty)
            {
                ClearRuleOutput(SelectedPlan, structureOut);
                return;
            }

            structureOut = EnsureHighResolution(structureOut);
            baseStr = EnsureHighResolution(baseStr);

            var combinedVolume = baseStr.SegmentVolume;
            for (int i = 1; i < inputStructures.Count; i++)
            {
                Structure s = inputStructures[i];
                if (s.IsEmpty)
                {
                    continue;
                }

                s = EnsureHighResolution(s);
                combinedVolume = combinedVolume.Sub(s);
            }

            structureOut.SegmentVolume = combinedVolume;
        }

        public static void ApplyAdditionMulti(PlanSetup SelectedPlan, string structureOutId, IList<string> structureInIds)
        {
            if (structureInIds == null || structureInIds.Count == 0) return;

            var nonEmptyInputs = structureInIds
                .Select(id => FindRequiredInputStructure(SelectedPlan, id))
                .Where(structure => !structure.IsEmpty)
                .ToList();
            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId);
            if (nonEmptyInputs.Count == 0)
            {
                ClearRuleOutput(SelectedPlan, structureOut);
                return;
            }

            structureOut = EnsureHighResolution(structureOut);
            Structure first = EnsureHighResolution(nonEmptyInputs[0]);
            var combinedVolume = first.SegmentVolume;
            for (int i = 1; i < nonEmptyInputs.Count; i++)
            {
                Structure structure = EnsureHighResolution(nonEmptyInputs[i]);
                combinedVolume = combinedVolume.Or(structure);
            }

            structureOut.SegmentVolume = combinedVolume;
        }

        public static void ApplyIntersectionMulti(PlanSetup SelectedPlan, string structureOutId, IList<string> structureInIds)
        {
            if (structureInIds == null || structureInIds.Count == 0) return;

            var inputStructures = structureInIds
                .Select(id => FindRequiredInputStructure(SelectedPlan, id))
                .ToList();
            Structure structureOut = FindStructureFromId(SelectedPlan, structureOutId);
            if (inputStructures.Any(structure => structure.IsEmpty))
            {
                ClearRuleOutput(SelectedPlan, structureOut);
                return;
            }

            structureOut = EnsureHighResolution(structureOut);
            Structure first = EnsureHighResolution(inputStructures[0]);
            var combinedVolume = first.SegmentVolume;
            for (int i = 1; i < inputStructures.Count; i++)
            {
                Structure structure = EnsureHighResolution(inputStructures[i]);
                combinedVolume = combinedVolume.And(structure);
            }

            structureOut.SegmentVolume = combinedVolume;
        }

        public static void ApplySbrtRing(PlanSetup SelectedPlan, string structureIn1Id, string structureIn2Id)
        {
            Structure ptv = FindRequiredInputStructure(SelectedPlan, structureIn1Id);
            Structure itv = FindRequiredInputStructure(SelectedPlan, structureIn2Id);
            RejectEmptySafetyCriticalMarginInput(itv);
            Structure ptv_ph = FindStructureFromId(SelectedPlan, structureIn1Id + "_Ph");
            if (ptv.IsEmpty)
            {
                ClearRuleOutput(SelectedPlan, ptv_ph);
            }
            else if (itv.IsEmpty)
            {
                ptv_ph.SegmentVolume = ptv.SegmentVolume;
            }
            else
            {
                ptv_ph.SegmentVolume = ptv.SegmentVolume.Sub(itv.SegmentVolume.Margin(1));
            }
        }

        public static void ApplyRectalWall(PlanSetup SelectedPlan)
        {
            Structure body = FindRequiredInputStructure(SelectedPlan, "BODY");
            Structure rectum = FindRequiredInputStructure(SelectedPlan, "Rectum");
            RejectEmptySafetyCriticalMarginInput(body);
            Structure bodyHR_Ph = FindStructureFromId(SelectedPlan, "BodyHR_Ph");
            Structure rectalWall = FindStructureFromId(SelectedPlan, "RectalWall_Ph");
            Structure rectalWallHelp = FindStructureFromId(SelectedPlan, "RectalWallHelp_Ph");

            if (rectum.IsEmpty)
            {
                ClearRuleOutput(SelectedPlan, rectalWall);
                SelectedPlan.StructureSet.RemoveStructure(bodyHR_Ph);
                SelectedPlan.StructureSet.RemoveStructure(rectalWallHelp);
                return;
            }

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

        private static void ApplyCtv1(PlanSetup selectedPlan, string targetId)
        {
            const double marginMm = 10;
            StructureSet structureSet = selectedPlan.StructureSet;
            Structure prostate = FindRequiredInputStructure(selectedPlan, Ctv1Rule.Prostate);
            Structure seminalVesicle = FindRequiredInputStructure(selectedPlan, Ctv1Rule.SeminalVesicle);
            foreach (Structure input in new[] { prostate, seminalVesicle })
            {
                if (input.IsEmpty)
                {
                    throw new InvalidOperationException($"Create CTV1 requires geometry in '{input.Id}'.");
                }
            }

            var outputs = Ctv1Rule.GetOutputIds(targetId)
                .Select((id, index) => (
                    Id: id,
                    Type: index < 2 ? "CONTROL" : "CTV",
                    Existing: FindExistingStructure(structureSet, id)))
                .ToArray();
            foreach (var output in outputs)
            {
                Structure existing = output.Existing;
                if (existing == null)
                {
                    if (!structureSet.CanAddStructure(output.Type, output.Id))
                    {
                        throw new InvalidOperationException($"ESAPI cannot create '{output.Id}' with Volume Type '{output.Type}'.");
                    }

                    continue;
                }

                if (output.Type == "CTV")
                {
                    VerifyVolumeType(existing, output.Type);
                }

                if (existing.IsApproved)
                {
                    throw new InvalidOperationException($"Create CTV1 cannot update approved structure '{existing.Id}'.");
                }

                if (!existing.CanEditSegmentVolume(out string error))
                {
                    throw new InvalidOperationException($"Create CTV1 cannot edit '{existing.Id}': {error}");
                }
            }

            bool useHighResolution = prostate.IsHighResolution || seminalVesicle.IsHighResolution ||
                outputs.Any(output => output.Existing?.IsHighResolution == true);
            var temporaryStructures = new List<Structure>();
            try
            {
                SegmentVolume prostateVolume = GetCtv1InputVolume(structureSet, prostate, useHighResolution, temporaryStructures);
                SegmentVolume seminalVesicleVolume = GetCtv1InputVolume(structureSet, seminalVesicle, useHighResolution, temporaryStructures);
                var margins = new AxisAlignedMargins(StructureMarginGeometry.Outer, 0, 0, 0, 0, marginMm, marginMm);
                SegmentVolume expandedProstate = prostateVolume.AsymmetricMargin(margins);
                SegmentVolume seminalVesicleBase = expandedProstate.And(seminalVesicleVolume);
                SegmentVolume ctv = prostateVolume.Or(seminalVesicleBase);
                SegmentVolume cleanedCtv = ctv.Margin(marginMm).Margin(-marginMm);
                var volumes = new[] { expandedProstate, seminalVesicleBase, ctv, cleanedCtv };

                var structures = outputs.Select(output =>
                    output.Existing ?? structureSet.AddStructure(output.Type, output.Id)).ToArray();
                if (useHighResolution)
                {
                    foreach (Structure structure in structures)
                    {
                        ConvertCtv1StructureToHighResolution(structure);
                    }
                }

                for (int index = 0; index < structures.Length; index++)
                {
                    structures[index].SegmentVolume = volumes[index];
                }

                if (structures[2].IsEmpty || structures[3].IsEmpty)
                {
                    throw new InvalidOperationException("Create CTV1 produced an empty target or cleaned contour.");
                }
            }
            finally
            {
                foreach (Structure structure in temporaryStructures)
                {
                    structureSet.RemoveStructure(structure);
                }
            }
        }

        private static SegmentVolume GetCtv1InputVolume(
            StructureSet structureSet, Structure source, bool useHighResolution, ICollection<Structure> temporaryStructures)
        {
            if (!useHighResolution || source.IsHighResolution)
            {
                return source.SegmentVolume;
            }

            // Match the Boolean operands' resolutions without modifying the source contour.
            string temporaryId = "Ctv1_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            Structure copy = structureSet.AddStructure("CONTROL", temporaryId);
            temporaryStructures.Add(copy);
            copy.SegmentVolume = source.SegmentVolume;
            ConvertCtv1StructureToHighResolution(copy);
            return copy.SegmentVolume;
        }

        private static void ConvertCtv1StructureToHighResolution(Structure structure)
        {
            if (!structure.IsHighResolution && structure.CanConvertToHighResolution())
            {
                structure.ConvertToHighResolution();
            }

            if (!structure.IsHighResolution)
            {
                throw new InvalidOperationException($"Create CTV1 could not convert '{structure.Id}' to high resolution.");
            }
        }

        private static Structure FindRequiredInputStructure(
            PlanSetup selectedPlan,
            string structureId)
        {
            Structure structure = FindExistingStructure(
                selectedPlan.StructureSet,
                structureId);
            if (structure == null)
            {
                throw new InvalidOperationException(
                    $"Required input structure '{structureId}' is missing. Input structures " +
                    "cannot be created without matching base-plan metadata.");
            }

            return structure;
        }

        private static void RejectEmptySafetyCriticalMarginInput(
            Structure structure)
        {
            if (structure.IsEmpty &&
                StructureMetadataPreflight.IsSafetyCriticalMarginInputType(
                    structure.DicomType))
            {
                throw new InvalidOperationException(
                    $"Required margin input structure '{structure.Id}' is empty. A " +
                    $"{structure.DicomType} structure used for margin generation must contain " +
                    "geometry.");
            }
        }

        private static void ClearRuleOutput(
            PlanSetup selectedPlan,
            Structure structure)
        {
            if (structure == null)
            {
                throw new InvalidOperationException(
                    "A rule output structure was not available for clearing.");
            }

            if (structure.IsEmpty)
            {
                return;
            }

            if (!structure.CanEditSegmentVolume(out string editError))
            {
                throw new InvalidOperationException(
                    $"Rule output structure '{structure.Id}' must become empty but cannot be " +
                    $"edited: {editError}");
            }

            Image image = selectedPlan.StructureSet.Image;
            if (image == null)
            {
                throw new InvalidOperationException(
                    $"Rule output structure '{structure.Id}' cannot be cleared because its " +
                    "structure set has no image.");
            }

            for (int imagePlane = 0; imagePlane < image.ZSize; imagePlane++)
            {
                structure.ClearAllContoursOnImagePlane(imagePlane);
            }

            if (!structure.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Rule output structure '{structure.Id}' could not be cleared completely.");
            }
        }

        public static Structure FindStructureFromId(PlanSetup SelectedPlan, string structureID, bool warnIfMissing, bool warnIfNotEmpty)
        {
            var structureSet = SelectedPlan.StructureSet;
            var outStructure = FindExistingStructure(structureSet, structureID);

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
