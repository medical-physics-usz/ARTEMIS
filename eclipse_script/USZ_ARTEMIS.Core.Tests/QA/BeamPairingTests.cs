using System.Linq;
using USZ_ARTEMIS.Core.QA;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.QA;

public sealed class BeamPairingTests
{
    [Fact]
    public void Pairs_reordered_reference_beams_with_the_correct_selected_fields()
    {
        var selected = new[] { (Id: "A1", Mmo: 12.5), (Id: "A2", Mmo: 20.0) };
        var reference = new[] { (Id: "A2", Mmo: 18.0), (Id: "A1", Mmo: 14.0) };

        var pairs = BeamPairing.ById(selected, reference, beam => beam.Id).ToArray();

        Assert.Collection(pairs,
            pair =>
            {
                Assert.Equal("A1", pair.Id);
                Assert.Equal(12.5, Assert.Single(pair.Selected).Mmo);
                Assert.Equal(14.0, Assert.Single(pair.Reference).Mmo);
            },
            pair =>
            {
                Assert.Equal("A2", pair.Id);
                Assert.Equal(20.0, Assert.Single(pair.Selected).Mmo);
                Assert.Equal(18.0, Assert.Single(pair.Reference).Mmo);
            });
    }

    [Fact]
    public void Keeps_unmatched_fields_in_both_directions_with_their_available_values()
    {
        var selected = new[] { (Id: "A1", Mmo: 12.5), (Id: "A2", Mmo: 20.0), (Id: "A3", Mmo: 15.0) };
        var reference = new[] { (Id: "A1", Mmo: 14.0), (Id: "a2", Mmo: 18.0) };

        var pairs = BeamPairing.ById(selected, reference, beam => beam.Id).ToArray();

        Assert.Equal(new[] { "A1", "A2", "A3", "a2" }, pairs.Select(pair => pair.Id));
        Assert.Equal(14.0, Assert.Single(pairs[0].Reference).Mmo);
        Assert.Equal(20.0, Assert.Single(pairs[1].Selected).Mmo);
        Assert.Empty(pairs[1].Reference);
        Assert.Empty(pairs[2].Reference);
        Assert.Empty(pairs[3].Selected);
        Assert.Equal(18.0, Assert.Single(pairs[3].Reference).Mmo);
    }

    [Fact]
    public void Retains_duplicate_ids_so_the_report_can_flag_ambiguity()
    {
        var selected = new[] { (Id: "A1", Mmo: 12.5) };
        var reference = new[] { (Id: "A1", Mmo: 14.0), (Id: "A1", Mmo: 18.0) };

        var pair = Assert.Single(BeamPairing.ById(selected, reference, beam => beam.Id));

        Assert.Equal(12.5, Assert.Single(pair.Selected).Mmo);
        Assert.Equal(new[] { 14.0, 18.0 }, pair.Reference.Select(beam => beam.Mmo));
    }
}
