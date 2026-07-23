using USZ_ARTEMIS.Core.Planning;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Planning;

public sealed class PtvContainmentSelectionTests
{
    [Theory]
    [InlineData("PTV1", "PTV", false, true)]
    [InlineData("ptv_boost", "ptv", false, true)]
    [InlineData("CTV1", "PTV", false, false)]
    [InlineData("PlanPTV", "PTV", false, false)]
    [InlineData("", "PTV", false, false)]
    [InlineData("PTV1", "CTV", false, false)]
    [InlineData("PTV1", "PTV", true, false)]
    public void Candidate_requires_non_empty_ptv_with_ptv_id_prefix(
        string structureId,
        string dicomType,
        bool isEmpty,
        bool expected)
    {
        Assert.Equal(
            expected,
            PtvContainmentSelection.IsCandidate(structureId, dicomType, isEmpty));
    }
}
