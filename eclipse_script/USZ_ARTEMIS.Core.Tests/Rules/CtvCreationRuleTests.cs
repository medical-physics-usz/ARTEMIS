using USZ_ARTEMIS.Actions;
using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Rules;

public class CtvCreationRuleTests
{
    [Theory]
    [InlineData("CTV1", "CTV1", "CTV1_closed")]
    [InlineData(" AdaptedCTV ", "AdaptedCTV", "AdaptedCTV_closed")]
    public void UsesTargetNameForBothCtvOutputs(string targetId, string expectedTarget, string expectedClosedTarget)
    {
        RuleStructureUse use = CtvCreationRule.Ctv1.GetStructureUse(targetId);

        Assert.Equal(new[] { "Prostate", "SeminalVesicle" }, use.InputIds);
        Assert.Equal(use.InputIds, use.GeometryRequiredInputIds);
        Assert.Equal(
            new[] { "Prostate+1cm_Ph", "SeminalVesicleBase1cm_Ph", expectedTarget, expectedClosedTarget },
            use.OutputIds);
        Assert.Empty(CtvCreationRule.Ctv1.GetTargetIdError(targetId));
    }

    [Fact]
    public void Ctv2UsesItsOwnIntermediatesAndSharesTheClosingMargin()
    {
        RuleStructureUse use = CtvCreationRule.Ctv2.GetStructureUse("AdaptedCTV2");

        Assert.Equal(new[] { "Prostate", "SeminalVesicle" }, use.InputIds);
        Assert.Equal(use.InputIds, use.GeometryRequiredInputIds);
        Assert.Equal(
            new[] { "Prostate+2cm_Ph", "SeminalVesicleBase2cm_Ph", "AdaptedCTV2", "AdaptedCTV2_closed" },
            use.OutputIds);
        Assert.Equal(10, CtvCreationRule.Ctv1.ExpansionMm);
        Assert.Equal(20, CtvCreationRule.Ctv2.ExpansionMm);
        Assert.Equal(10, CtvCreationRule.ClosingMarginMm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(" prostate ")]
    [InlineData("SEMINALVESICLE")]
    [InlineData("Prostate+1cm_Ph")]
    [InlineData("seminalvesiclebase1cm_ph")]
    [InlineData("Prostate+2cm_Ph")]
    [InlineData("seminalvesiclebase2cm_ph")]
    public void RejectsMissingTargetsAndTargetsThatWouldOverwriteAnotherStructure(string? targetId)
    {
        Assert.NotEmpty(CtvCreationRule.Ctv1.GetTargetIdError(targetId!));
        Assert.NotEmpty(CtvCreationRule.Ctv2.GetTargetIdError(targetId!));
    }

    [Theory]
    [InlineData(" ctv1 ")]
    [InlineData(" ctv1_CLOSED ")]
    [InlineData("Prostate+1cm_Ph")]
    [InlineData("SeminalVesicleBase1cm_Ph")]
    public void InsertsAfterAnatomyProducerAndBeforeFirstOutputConsumer(string consumedOutput)
    {
        RuleStructureUse[] rules =
        [
            new(["ProstateRaw"], ["Prostate"]),
            new(["Rectum", consumedOutput], ["Rectum"]),
            new(["CTV1"], ["PTV1"])
        ];

        Assert.Equal(1, CtvCreationRule.Ctv1.FindInsertionIndex(rules, "CTV1"));
    }

    [Fact]
    public void InsertsCtv2BeforeItsConsumersInARuleSetWithCtv1()
    {
        RuleStructureUse[] rules =
        [
            CtvCreationRule.Ctv1.GetStructureUse("CTV1"),
            new(["CTV1"], ["PTV1"]),
            new(["CTV2_closed"], ["PTV2"])
        ];

        Assert.Equal(2, CtvCreationRule.Ctv2.FindInsertionIndex(rules, "CTV2"));
    }

    [Fact]
    public void AppendsWhenNoExistingRuleUsesAnOutput()
    {
        RuleStructureUse[] rules = [new(["Rectum"], ["RectalWall_Ph"])];

        Assert.Equal(1, CtvCreationRule.Ctv1.FindInsertionIndex(rules, "CTV1"));
    }

    [Fact]
    public void RejectsInsertionWhenAnatomyIsProducedAfterAnOutputConsumer()
    {
        RuleStructureUse[] rules =
        [
            new(["CTV1"], ["PTV1"]),
            new(["SeminalVesicleRaw"], [" seminalvesicle "])
        ];

        Assert.Equal(-1, CtvCreationRule.Ctv1.FindInsertionIndex(rules, "CTV1"));
    }
}
