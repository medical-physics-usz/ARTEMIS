using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace USZ_ARTEMIS.Tools
{
    internal static class DoseCheckLauncher
    {
        internal const string AccelaMachineId = "Accela1008";

        internal static bool IsAccelaMachine(string machineId)
        {
            return string.Equals(machineId, AccelaMachineId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSciMoCaMachine(string machineId)
        {
            return string.Equals(machineId, "Edge2018", StringComparison.OrdinalIgnoreCase)
                || string.Equals(machineId, "TrueBeam3232", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool UseDoseCheck(string machineSelection, IEnumerable<string> treatmentMachineIds)
        {
            if (!IsAccelaMachine(machineSelection) && !IsSciMoCaMachine(machineSelection))
                throw new InvalidOperationException("Select a treatment machine before running QA.");

            var machines = treatmentMachineIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (machines.Count != 1 || (!IsAccelaMachine(machines[0]) && !IsSciMoCaMachine(machines[0])))
                throw new InvalidOperationException(
                    "The open plan must have treatment beams for a single supported machine before running QA.");

            bool useDoseCheck = IsAccelaMachine(machineSelection);
            if (useDoseCheck != IsAccelaMachine(machines[0]))
                throw new InvalidOperationException(
                    $"The open plan uses {machines[0]} and the selected machine is {machineSelection}. " +
                    "Select the plan's treatment machine or open the intended plan in Eclipse.");

            return useDoseCheck;
        }

        internal static ProcessStartInfo CreateStartInfo(
            string executablePath, string workingDirectory,
            string patientId, string planLabel, string planUid, string doseUid, string username, bool waterPsqa)
        {
            var arguments = new[] { patientId, planLabel, planUid, doseUid, username };
            if (arguments.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException(
                    "DoseCHECK requires an open patient and plan, the plan and calculated dose DICOM UIDs, and the Windows username.");
            if (waterPsqa && planLabel.Trim().Length > 13)
                throw new InvalidOperationException(
                    "Water PSQA requires a plan label of at most 13 characters so the exported label can end in _QW.");

            if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(workingDirectory))
            {
                string prefix = waterPsqa ? "DoseCheckPsqa" : "DoseCheck";
                throw new InvalidOperationException(
                    $"Set {prefix}ExecutablePath and {prefix}WorkingDirectory in the ARTEMIS path configuration.");
            }
            if (!ExternalProcessLauncher.IsAbsolutePath(executablePath) || !ExternalProcessLauncher.IsAbsolutePath(workingDirectory))
                throw new InvalidOperationException("DoseCHECK executable and working directory paths must be absolute.");
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("DoseCHECK executable not found or inaccessible:\n" + executablePath);
            if (!Directory.Exists(workingDirectory))
                throw new DirectoryNotFoundException("DoseCHECK working directory not found or inaccessible:\n" + workingDirectory);

            return new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                Arguments = string.Join(" ", arguments.Select(value => ExternalProcessLauncher.QuoteArgument(value.Trim()))),
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
