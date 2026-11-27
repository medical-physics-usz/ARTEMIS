using System;
using System.Windows;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.Tools;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private async void BtnLaunchPreprocessing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(AppPaths.ConfigurationLoadError))
                    throw new InvalidOperationException(AppPaths.ConfigurationLoadError);

                var plan = GetSelectedPlan();
                var startInfo = PreprocessingLauncher.CreateStartInfo(
                    AppPaths.PreprocessingExecutablePath,
                    AppPaths.PreprocessingWorkingDirectory,
                    context.Patient?.Id, plan?.Id, plan?.UID, Environment.UserName);

                int exitCode = await ExternalProcessLauncher.RunAsync(startInfo);
                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"Preprocessing exited with code {exitCode}. If its window did not open, verify that " +
                        "the .env file beside the executable defines INPUT_DIR and BASEPLAN_DIR.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not launch ARTEMIS preprocessing.\n\n" + ex.Message +
                    "\n\nPath configuration: " + (AppPaths.ConfigurationSourcePath ?? "USZ_ARTEMIS.esapi.json"),
                    "ARTEMIS preprocessing launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
