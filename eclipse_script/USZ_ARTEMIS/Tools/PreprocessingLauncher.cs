using System;
using System.Diagnostics;
using System.IO;

namespace USZ_ARTEMIS.Tools
{
    internal static class PreprocessingLauncher
    {
        internal static ProcessStartInfo CreateStartInfo(
            string executablePath, string workingDirectory,
            string patientId, string planId, string planUid, string username)
        {
            if (string.IsNullOrWhiteSpace(patientId))
                throw new InvalidOperationException("Please select a patient in Eclipse before launching preprocessing.");
            if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(planUid))
                throw new InvalidOperationException("Please open a plan in Eclipse before launching preprocessing.");
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("The Windows username is unavailable.");

            if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(workingDirectory))
                throw new InvalidOperationException(
                    "Set PreprocessingExecutablePath and PreprocessingWorkingDirectory in the ARTEMIS path configuration.");
            if (!ExternalProcessLauncher.IsAbsolutePath(executablePath) || !ExternalProcessLauncher.IsAbsolutePath(workingDirectory))
                throw new InvalidOperationException("Preprocessing paths must be absolute paths.");
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("Preprocessing executable not found or inaccessible:\n" + executablePath);
            if (!Directory.Exists(workingDirectory))
                throw new DirectoryNotFoundException("Preprocessing working directory not found or inaccessible:\n" + workingDirectory);

            return new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                // Keep the standalone launcher's four positional arguments.
                Arguments = string.Join(" ", new[]
                {
                    ExternalProcessLauncher.QuoteArgument(patientId.Trim()), ExternalProcessLauncher.QuoteArgument(planId.Trim()),
                    ExternalProcessLauncher.QuoteArgument(planUid.Trim()), ExternalProcessLauncher.QuoteArgument(username.Trim())
                }),
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
