using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using USZ_ARTEMIS.Tools;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Launching;

public sealed class PreprocessingLauncherTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "artemis-launch-test-" + Guid.NewGuid());
    private readonly string executable;

    public PreprocessingLauncherTests()
    {
        Directory.CreateDirectory(folder);
        executable = Path.Combine(folder, "synthetic preprocessing.exe");
        // A placeholder only; no process or clinical endpoint is started by these tests.
        File.WriteAllText(executable, string.Empty);
    }

    [Fact]
    public void Preserves_four_argument_contract_and_configured_working_directory()
    {
        var info = PreprocessingLauncher.CreateStartInfo(
            executable, folder, " SYNTHETIC 001 ", " Test Plan ", " 2.25.123 ", " test user ");

        Assert.Equal(executable, info.FileName);
        Assert.Equal(folder, info.WorkingDirectory);
        Assert.Equal("\"SYNTHETIC 001\" \"Test Plan\" \"2.25.123\" \"test user\"", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Returns_the_exit_code_even_when_the_process_exits_immediately(int exitCode)
    {
        bool isWindows = OperatingSystem.IsWindows();
        var startInfo = new ProcessStartInfo
        {
            FileName = isWindows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
            Arguments = isWindows ? $"/d /c exit {exitCode}" : $"-c \"exit {exitCode}\"",
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Assert.Equal(exitCode, await ExternalProcessLauncher.RunAsync(startInfo).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("Test \"Plan\"", "\"Test \\\"Plan\\\"\"")]
    [InlineData("Test\\", "\"Test\\\\\"")]
    [InlineData("Test\\\"Plan", "\"Test\\\\\\\"Plan\"")]
    [InlineData("Test\\Folder", "\"Test\\Folder\"")]
    [InlineData("Plan ü & review", "\"Plan ü & review\"")]
    public void Quotes_windows_arguments_without_changing_their_values(string planId, string expected)
    {
        var info = PreprocessingLauncher.CreateStartInfo(
            executable, folder, "SYNTHETIC", planId, "2.25.123", "test-user");

        Assert.Equal("\"SYNTHETIC\" " + expected + " \"2.25.123\" \"test-user\"", info.Arguments);
    }

    [Theory]
    [InlineData(null, "Test Plan", "2.25.123", "test-user", "select a patient")]
    [InlineData(" ", "Test Plan", "2.25.123", "test-user", "select a patient")]
    [InlineData("SYNTHETIC", null, "2.25.123", "test-user", "open a plan")]
    [InlineData("SYNTHETIC", "Test Plan", " ", "test-user", "open a plan")]
    [InlineData("SYNTHETIC", "Test Plan", "2.25.123", "", "username")]
    public void Rejects_missing_context_before_creating_a_launch_request(
        string? patientId, string? planId, string? planUid, string username, string message)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PreprocessingLauncher.CreateStartInfo(
            executable, folder, patientId!, planId!, planUid!, username));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "configured-folder")]
    [InlineData("configured-executable", " ")]
    public void Requires_both_configuration_values(string executablePath, string workingDirectory)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PreprocessingLauncher.CreateStartInfo(
            executablePath, workingDirectory, "SYNTHETIC", "Test Plan", "2.25.123", "test-user"));

        Assert.Contains("PreprocessingExecutablePath and PreprocessingWorkingDirectory", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rejects_relative_paths(bool relativeExecutable)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PreprocessingLauncher.CreateStartInfo(
            relativeExecutable ? "synthetic.exe" : executable,
            relativeExecutable ? folder : "relative-folder",
            "SYNTHETIC", "Test Plan", "2.25.123", "test-user"));

        Assert.Contains("absolute paths", exception.Message);
    }

    [Fact]
    public void Rejects_missing_executable()
    {
        File.Delete(executable);

        Assert.Throws<FileNotFoundException>(() => PreprocessingLauncher.CreateStartInfo(
            executable, folder, "SYNTHETIC", "Test Plan", "2.25.123", "test-user"));
    }

    [Theory]
    [InlineData("C:synthetic.exe")]
    [InlineData("\\synthetic.exe")]
    public void Rejects_windows_paths_that_depend_on_the_current_directory_or_drive(string path)
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        Assert.Throws<InvalidOperationException>(() => PreprocessingLauncher.CreateStartInfo(
            path, folder, "SYNTHETIC", "Test Plan", "2.25.123", "test-user"));
    }

    [Fact]
    public void Rejects_missing_working_directory()
    {
        Assert.Throws<DirectoryNotFoundException>(() => PreprocessingLauncher.CreateStartInfo(
            executable, Path.Combine(folder, "missing"), "SYNTHETIC", "Test Plan", "2.25.123", "test-user"));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
