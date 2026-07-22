using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Rules;

public sealed class RuleStructureResolutionPolicyTests
{
    [Theory]
    [InlineData("Bowel")]
    [InlineData("sigma")]
    [InlineData("RECTUM")]
    [InlineData("Bladder")]
    [InlineData("GTV")]
    [InlineData("GTV_1")]
    [InlineData("itvBoost")]
    [InlineData("PTV+2cm_Ph")]
    public void RequiresHighResolution_IncludesConfiguredIdsAndPrefixes(string structureId)
    {
        Assert.True(RuleStructureResolutionPolicy.RequiresHighResolution(structureId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BowelBag")]
    [InlineData("XPTV")]
    [InlineData("CTV")]
    [InlineData("Body")]
    public void RequiresHighResolution_ExcludesOtherIds(string? structureId)
    {
        Assert.False(RuleStructureResolutionPolicy.RequiresHighResolution(structureId!));
    }
}
