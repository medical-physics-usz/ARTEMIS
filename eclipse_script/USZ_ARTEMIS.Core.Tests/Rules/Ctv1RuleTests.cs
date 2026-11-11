using USZ_ARTEMIS.Actions;
using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Rules;

public class Ctv1RuleTests
{
    [Fact]
    public void UsesSelectedTargetAndFixedNamesForOtherOutputs()
    {
        RuleStructureUse use = Ctv1Rule.GetStructureUse("AdaptedCTV");

        Assert.Equal(new[] { "Prostate", "SeminalVesicle" }, use.InputIds);
        Assert.Equal(use.InputIds, use.GeometryRequiredInputIds);
        Assert.Equal(
            new[] { "Prostate+1cm_Ph", "SeminalVesicleBase1cm_Ph", "AdaptedCTV", "CTV1_clean" },
            use.OutputIds);
        Assert.Empty(Ctv1Rule.GetTargetIdError("AdaptedCTV"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(" prostate ")]
    [InlineData("SEMINALVESICLE")]
    [InlineData("Prostate+1cm_Ph")]
    [InlineData("seminalvesiclebase1cm_ph")]
    [InlineData("ctv1_clean")]
    public void RejectsMissingTargetsAndTargetsThatWouldOverwriteAnotherStructure(string? targetId)
    {
        Assert.NotEmpty(Ctv1Rule.GetTargetIdError(targetId!));
    }

    [Theory]
    [InlineData(" ctv1 ")]
    [InlineData("CTV1_clean")]
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

        Assert.Equal(1, Ctv1Rule.FindInsertionIndex(rules, "CTV1"));
    }

    [Fact]
    public void AppendsWhenNoExistingRuleUsesAnOutput()
    {
        RuleStructureUse[] rules = [new(["Rectum"], ["RectalWall_Ph"])];

        Assert.Equal(1, Ctv1Rule.FindInsertionIndex(rules, "CTV1"));
    }

    [Fact]
    public void RejectsInsertionWhenAnatomyIsProducedAfterAnOutputConsumer()
    {
        RuleStructureUse[] rules =
        [
            new(["CTV1"], ["PTV1"]),
            new(["SeminalVesicleRaw"], [" seminalvesicle "])
        ];

        Assert.Equal(-1, Ctv1Rule.FindInsertionIndex(rules, "CTV1"));
    }
}
