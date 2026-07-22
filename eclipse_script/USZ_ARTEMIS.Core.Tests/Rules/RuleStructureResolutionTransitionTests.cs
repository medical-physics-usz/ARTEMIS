using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Rules;

public sealed class RuleStructureResolutionTransitionTests
{
    [Theory]
    [InlineData(false, false, false, RuleStructureResolutionTransition.LowAtRulePreflight)]
    [InlineData(true, true, true, RuleStructureResolutionTransition.HighBeforePlanCopy)]
    [InlineData(false, true, true, RuleStructureResolutionTransition.ChangedDuringPlanCopy)]
    [InlineData(false, false, true, RuleStructureResolutionTransition.ChangedAfterPlanCopyBeforeRulePreflight)]
    public void Classify_WithPlanCopyCheckpoints_IdentifiesTransitionStage(
        bool beforePlanCopy,
        bool afterPlanCopy,
        bool atRulePreflight,
        RuleStructureResolutionTransition expected)
    {
        Assert.Equal(
            expected,
            RuleStructureResolutionTransitionClassifier.Classify(
                beforePlanCopy,
                afterPlanCopy,
                atRulePreflight));
    }

    [Theory]
    [InlineData(null, true, true, RuleStructureResolutionTransition.HighByAfterPlanCopy)]
    [InlineData(null, null, true, RuleStructureResolutionTransition.HighAtRulePreflightOnly)]
    public void Classify_WithMissingCheckpoints_ReportsAvailableEvidence(
        bool? beforePlanCopy,
        bool? afterPlanCopy,
        bool atRulePreflight,
        RuleStructureResolutionTransition expected)
    {
        Assert.Equal(
            expected,
            RuleStructureResolutionTransitionClassifier.Classify(
                beforePlanCopy,
                afterPlanCopy,
                atRulePreflight));
    }
}
