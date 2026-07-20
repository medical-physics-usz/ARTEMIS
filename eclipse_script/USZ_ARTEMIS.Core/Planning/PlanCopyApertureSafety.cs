using System;
using System.Collections.Generic;
using System.Linq;

namespace USZ_ARTEMIS.Core.Planning
{
    public sealed class PlanCopyApertureSafety
    {
        private PlanCopyApertureSafety(bool allowAutomaticOptimization, string warningMessage)
        {
            AllowAutomaticOptimization = allowAutomaticOptimization;
            WarningMessage = warningMessage;
        }

        public bool AllowAutomaticOptimization { get; }

        public string WarningMessage { get; }

        public static PlanCopyApertureSafety FromContainmentCheck(
            string ringId,
            IEnumerable<string> ptvIdsOutsideRing)
        {
            if (ptvIdsOutsideRing == null)
            {
                throw new ArgumentNullException(nameof(ptvIdsOutsideRing));
            }

            var outsideIds = ptvIdsOutsideRing
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (outsideIds.Count == 0)
            {
                return new PlanCopyApertureSafety(true, null);
            }

            return new PlanCopyApertureSafety(
                false,
                $"The following PTV contours extend outside the +2 cm ring '{ringId}': " +
                $"{string.Join(", ", outsideIds)}.\n\n" +
                "This suggests a suspicious, substantial difference in target segmentation compared with the base plan. " +
                "This may indicate an overcontoured target or an incorrectly propagated +2 cm ring.\n\n" +
                "Automatic jaw and aperture optimization has been skipped. " +
                "Please have the target and ring reviewed again by the MD, then manually adjust the jaws and aperture.");
        }

        public static PlanCopyApertureSafety FromFailedCheck(string ringId, string errorMessage)
        {
            string details = string.IsNullOrWhiteSpace(errorMessage)
                ? string.Empty
                : $"\n\nDetails: {errorMessage}";

            return new PlanCopyApertureSafety(
                false,
                $"ARTEMIS could not verify that all PTV contours are contained within the +2 cm ring '{ringId}'." +
                "\n\nAutomatic jaw and aperture optimization has been skipped. " +
                "Please review the target and ring, then manually adjust the jaws and aperture." +
                details);
        }
    }
}
