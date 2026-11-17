using System;
using System.Collections.Generic;
using System.Linq;
using USZ_ARTEMIS.Core.Rules;

namespace USZ_ARTEMIS.Actions
{
    internal sealed class CtvCreationRule
    {
        public const string Prostate = "Prostate";
        public const string SeminalVesicle = "SeminalVesicle";
        public const double ClosingMarginMm = 10;

        public static readonly CtvCreationRule Ctv1 = new CtvCreationRule(
            "Create CTV1", 10, "Prostate+1cm_Ph", "SeminalVesicleBase1cm_Ph");
        public static readonly CtvCreationRule Ctv2 = new CtvCreationRule(
            "Create CTV2", 20, "Prostate+2cm_Ph", "SeminalVesicleBase2cm_Ph");

        private CtvCreationRule(string name, double expansionMm, string expandedProstate, string seminalVesicleBase)
        {
            Name = name;
            ExpansionMm = expansionMm;
            ExpandedProstate = expandedProstate;
            SeminalVesicleBase = seminalVesicleBase;
        }

        public string Name { get; }
        public double ExpansionMm { get; }
        private string ExpandedProstate { get; }
        private string SeminalVesicleBase { get; }

        public string[] GetOutputIds(string targetId)
        {
            string target = (targetId ?? string.Empty).Trim();
            string closedTarget = target.Length == 0 ? string.Empty : target + "_closed";
            return new[] { ExpandedProstate, SeminalVesicleBase, target, closedTarget };
        }

        public string GetTargetIdError(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return $"Select or enter a target structure for {Name}.";
            }

            var reservedIds = new[]
            {
                Prostate, SeminalVesicle,
                Ctv1.ExpandedProstate, Ctv1.SeminalVesicleBase,
                Ctv2.ExpandedProstate, Ctv2.SeminalVesicleBase
            };
            if (reservedIds.Contains(targetId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return $"'{targetId}' is reserved by the CTV creation rules. Choose a different target.";
            }

            return string.Empty;
        }

        public RuleStructureUse GetStructureUse(string targetId)
        {
            var inputs = new[] { Prostate, SeminalVesicle };
            return new RuleStructureUse(inputs, GetOutputIds(targetId), inputs);
        }

        public int FindInsertionIndex(IReadOnlyList<RuleStructureUse> existingRules, string targetId)
        {
            RuleStructureUse use = GetStructureUse(targetId);
            var outputIds = new HashSet<string>(use.OutputIds, StringComparer.OrdinalIgnoreCase);
            var inputIds = new HashSet<string>(use.InputIds, StringComparer.OrdinalIgnoreCase);
            int insertionIndex = 0;
            while (insertionIndex < existingRules.Count &&
                   !existingRules[insertionIndex].InputIds.Any(id => id != null && outputIds.Contains(id.Trim())))
            {
                insertionIndex++;
            }

            if (existingRules.Skip(insertionIndex).SelectMany(rule => rule.OutputIds)
                .Any(id => id != null && inputIds.Contains(id.Trim())))
            {
                return -1;
            }

            return insertionIndex;
        }
    }
}
