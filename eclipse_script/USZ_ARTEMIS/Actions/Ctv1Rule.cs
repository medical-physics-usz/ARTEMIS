using System;
using System.Collections.Generic;
using System.Linq;
using USZ_ARTEMIS.Core.Rules;

namespace USZ_ARTEMIS.Actions
{
    internal static class Ctv1Rule
    {
        public const string Prostate = "Prostate";
        public const string SeminalVesicle = "SeminalVesicle";
        public const string ExpandedProstate = "Prostate+1cm_Ph";
        public const string SeminalVesicleBase = "SeminalVesicleBase1cm_Ph";
        public const string CleanedCtv = "CTV1_clean";

        public static string[] GetOutputIds(string targetId)
            => new[] { ExpandedProstate, SeminalVesicleBase, targetId, CleanedCtv };

        public static string GetTargetIdError(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return "Select or enter a target structure for Create CTV1.";
            }

            var reservedIds = new[] { Prostate, SeminalVesicle, ExpandedProstate, SeminalVesicleBase, CleanedCtv };
            if (reservedIds.Contains(targetId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return $"'{targetId}' is already used as an input or a generated structure by Create CTV1. Choose a different target.";
            }

            return string.Empty;
        }

        public static RuleStructureUse GetStructureUse(string targetId)
        {
            var inputs = new[] { Prostate, SeminalVesicle };
            return new RuleStructureUse(inputs, GetOutputIds(targetId), inputs);
        }

        public static int FindInsertionIndex(IReadOnlyList<RuleStructureUse> existingRules, string targetId)
        {
            RuleStructureUse ctv1 = GetStructureUse(targetId);
            var outputIds = new HashSet<string>(ctv1.OutputIds, StringComparer.OrdinalIgnoreCase);
            var inputIds = new HashSet<string>(ctv1.InputIds, StringComparer.OrdinalIgnoreCase);
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
