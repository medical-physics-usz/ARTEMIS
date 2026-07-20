using System;

namespace USZ_ARTEMIS.Core.Rules
{
    public static class PlanIdUtilities
    {
        public static string GuessBasePlanId(string planId)
        {
            if (string.IsNullOrEmpty(planId))
            {
                return planId;
            }

            // Heuristic for copied plans, e.g. "...aA" -> "...a".
            if (planId.Length >= 2 &&
                planId[planId.Length - 2] == 'a' &&
                char.IsUpper(planId[planId.Length - 1]))
            {
                return planId.Substring(0, planId.Length - 1);
            }

            // Fallback heuristic for trailing fraction letters.
            if (planId.Length >= 2 && char.IsUpper(planId[planId.Length - 1]))
            {
                return planId.Substring(0, planId.Length - 1);
            }

            return planId;
        }
    }
}
