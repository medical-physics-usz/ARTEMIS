using USZ_ARTEMIS.Core.Planning;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Planning;

public sealed class PlanCopyNamesTests
{
    [Fact]
    public void Copy_names_are_preserved_when_plan_id_is_within_limit()
    {
        var result = PlanCopyNames.Create("SBabc01_1a", "Planabc Lung", "A");

        Assert.Equal("SBabc01_1aA", result.Id);
        Assert.Equal("Planabc LungA", result.Name);
        Assert.False(result.WasShortened);
    }

    [Fact]
    public void Copy_name_is_shortened_with_over_limit_plan_id()
    {
        var result = PlanCopyNames.Create("SBabc01-02_1a", "Planabc Lung", "A");

        Assert.Equal("SB01-02_1aA", result.Id);
        Assert.Equal("Plan LungA", result.Name);
        Assert.True(result.WasShortened);
    }

    [Fact]
    public void Short_plan_name_remains_valid_when_plan_id_is_shortened()
    {
        var result = PlanCopyNames.Create("SBabc01-02_1a", "SB", "A");

        Assert.Equal("SB01-02_1aA", result.Id);
        Assert.Equal("SBA", result.Name);
        Assert.True(result.WasShortened);
    }
}
