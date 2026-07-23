using System;
using System.Collections.Generic;
using System.Linq;

namespace USZ_ARTEMIS.Core.Planning
{
    public sealed class PlanCopyObjectiveFilter
    {
        private static readonly string[] TargetPrefixes = { "PTV", "ITV", "GTV" };

        private readonly HashSet<string> clinicalGoalStructureIds;

        public PlanCopyObjectiveFilter(IEnumerable<string> clinicalGoalStructureIds)
        {
            if (clinicalGoalStructureIds == null)
            {
                throw new ArgumentNullException(nameof(clinicalGoalStructureIds));
            }

            this.clinicalGoalStructureIds = new HashSet<string>(
                clinicalGoalStructureIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool ShouldCopy(string structureId, bool isMeanDoseObjective)
        {
            if (string.IsNullOrWhiteSpace(structureId))
            {
                return false;
            }

            string normalizedStructureId = structureId.Trim();
            if (TargetPrefixes.Any(prefix =>
                normalizedStructureId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return !isMeanDoseObjective && clinicalGoalStructureIds.Contains(normalizedStructureId);
        }
    }
}
