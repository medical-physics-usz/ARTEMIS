using USZ_ARTEMIS.Core.Planning;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Planning;

public sealed class PlanCopyObjectiveFilterTests
{
    [Fact]
    public void Copies_non_target_objective_when_structure_has_clinical_goal()
    {
        var filter = new PlanCopyObjectiveFilter(new[] { "SpinalCord" });

        Assert.True(filter.ShouldCopy("SpinalCord", isMeanDoseObjective: false));
    }

    [Fact]
    public void Matches_clinical_goal_structure_ids_case_insensitively_and_ignores_whitespace()
    {
        var filter = new PlanCopyObjectiveFilter(new[] { " spinalcord " });

        Assert.True(filter.ShouldCopy(" SPINALCORD ", isMeanDoseObjective: false));
    }

    [Fact]
    public void Skips_non_target_objective_when_structure_has_no_clinical_goal()
    {
        var filter = new PlanCopyObjectiveFilter(new[] { "SpinalCord" });

        Assert.False(filter.ShouldCopy("Parotid_L", isMeanDoseObjective: false));
    }

    [Fact]
    public void Skips_non_target_mean_dose_objective_even_when_structure_has_clinical_goal()
    {
        var filter = new PlanCopyObjectiveFilter(new[] { "Parotid_L" });

        Assert.False(filter.ShouldCopy("Parotid_L", isMeanDoseObjective: true));
    }

    [Theory]
    [InlineData("PTV1")]
    [InlineData("itv_Liver")]
    [InlineData(" GTVp ")]
    public void Always_copies_target_objectives_including_mean_dose_objectives(string structureId)
    {
        var filter = new PlanCopyObjectiveFilter(new string[0]);

        Assert.True(filter.ShouldCopy(structureId, isMeanDoseObjective: false));
        Assert.True(filter.ShouldCopy(structureId, isMeanDoseObjective: true));
    }

    [Fact]
    public void Skips_objective_with_missing_structure_id()
    {
        var filter = new PlanCopyObjectiveFilter(new[] { "SpinalCord" });

        Assert.False(filter.ShouldCopy(" ", isMeanDoseObjective: false));
    }
}
