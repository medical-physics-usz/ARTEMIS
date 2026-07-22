namespace USZ_ARTEMIS.Core.Rules
{
    public enum RuleStructureResolutionTransition
    {
        LowAtRulePreflight,
        HighBeforePlanCopy,
        ChangedDuringPlanCopy,
        ChangedAfterPlanCopyBeforeRulePreflight,
        HighByAfterPlanCopy,
        HighAtRulePreflightOnly
    }

    public static class RuleStructureResolutionTransitionClassifier
    {
        public static RuleStructureResolutionTransition Classify(
            bool? beforePlanCopy,
            bool? afterPlanCopy,
            bool atRulePreflight)
        {
            if (!atRulePreflight)
            {
                return RuleStructureResolutionTransition.LowAtRulePreflight;
            }

            if (afterPlanCopy == false)
            {
                return RuleStructureResolutionTransition.ChangedAfterPlanCopyBeforeRulePreflight;
            }

            if (beforePlanCopy == false && afterPlanCopy == true)
            {
                return RuleStructureResolutionTransition.ChangedDuringPlanCopy;
            }

            if (beforePlanCopy == true)
            {
                return RuleStructureResolutionTransition.HighBeforePlanCopy;
            }

            if (afterPlanCopy == true)
            {
                return RuleStructureResolutionTransition.HighByAfterPlanCopy;
            }

            return RuleStructureResolutionTransition.HighAtRulePreflightOnly;
        }
    }
}
