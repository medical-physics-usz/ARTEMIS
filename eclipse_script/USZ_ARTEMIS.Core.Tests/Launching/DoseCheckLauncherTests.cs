using System;
using System.IO;
using System.Linq;
using USZ_ARTEMIS.Tools;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Launching;

public sealed class DoseCheckLauncherTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "artemis-dosecheck-test-" + Guid.NewGuid());
    private readonly string executable;

    public DoseCheckLauncherTests()
    {
        Directory.CreateDirectory(folder);
        executable = Path.Combine(folder, "synthetic exporter.exe");
        File.WriteAllText(executable, string.Empty);
    }

    [Theory]
    [InlineData("Accela1008", "Accela1008", true)]
    [InlineData("Accela1008", "accela1008", true)]
    [InlineData("Edge2018", "Edge2018", false)]
    [InlineData("TrueBeam3232", "TrueBeam3232", false)]
    [InlineData("Edge2018", "TrueBeam3232", false)]
    public void Routes_supported_machines(string selection, string beamMachine, bool useDoseCheck)
    {
        Assert.Equal(useDoseCheck, DoseCheckLauncher.UseDoseCheck(selection, new[] { beamMachine, beamMachine }));
    }

    [Theory]
    [InlineData("Accela1008", "Edge2018")]
    [InlineData("TrueBeam3232", "Accela1008")]
    [InlineData("Accela1008", "UNKNOWN")]
    [InlineData("Accela1008", "")]
    [InlineData("", "Accela1008")]
    public void Rejects_an_unsupported_or_mismatched_plan(string selection, string beamMachine)
    {
        Assert.Throws<InvalidOperationException>(() => DoseCheckLauncher.UseDoseCheck(selection, new[] { beamMachine }));
    }

    [Fact]
    public void Rejects_mixed_machines_and_plans_without_treatment_beams()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DoseCheckLauncher.UseDoseCheck("Accela1008", new[] { "Accela1008", "Edge2018" }));
        Assert.Throws<InvalidOperationException>(() =>
            DoseCheckLauncher.UseDoseCheck("Accela1008", Enumerable.Empty<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Passes_the_five_exporter_arguments_in_order(bool waterPsqa)
    {
        var info = DoseCheckLauncher.CreateStartInfo(
            executable, folder, " TEST 001 ", "Test \"Plan\"", "2.25.1", "2.25.2", "test user", waterPsqa);

        Assert.Equal(executable, info.FileName);
        Assert.Equal(folder, info.WorkingDirectory);
        Assert.Equal("\"TEST 001\" \"Test \\\"Plan\\\"\" \"2.25.1\" \"2.25.2\" \"test user\"", info.Arguments);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void Only_water_psqa_reserves_three_characters_for_the_label_suffix()
    {
        DoseCheckLauncher.CreateStartInfo(executable, folder, "TEST", "1234567890123", "2.25.1", "2.25.2", "user", true);
        DoseCheckLauncher.CreateStartInfo(executable, folder, "TEST", "12345678901234", "2.25.1", "2.25.2", "user", false);
        Assert.Throws<InvalidOperationException>(() => DoseCheckLauncher.CreateStartInfo(
            executable, folder, "TEST", "12345678901234", "2.25.1", "2.25.2", "user", true));
    }

    [Fact]
    public void Requires_calculated_dose_and_the_selected_exporters_configuration()
    {
        Assert.Throws<InvalidOperationException>(() => DoseCheckLauncher.CreateStartInfo(
            executable, folder, "TEST", "Plan", "2.25.1", "", "user", false));
        var error = Assert.Throws<InvalidOperationException>(() => DoseCheckLauncher.CreateStartInfo(
            "", folder, "TEST", "Plan", "2.25.1", "2.25.2", "user", true));
        Assert.Contains("DoseCheckPsqaExecutablePath", error.Message);
        Assert.Throws<FileNotFoundException>(() => DoseCheckLauncher.CreateStartInfo(
            Path.Combine(folder, "missing.exe"), folder, "TEST", "Plan", "2.25.1", "2.25.2", "user", false));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
