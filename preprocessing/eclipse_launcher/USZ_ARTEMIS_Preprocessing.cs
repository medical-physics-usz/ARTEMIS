using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.IO;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Diagnostics;

namespace VMS.TPS
{
    public class Script
    {
        private const string ConfigFileName = "USZ_ARTEMIS_Preprocessing.local.config";

        public Script()
        {
        }

        public void Execute(ScriptContext context)
        {
            // Get patient id.
            Patient patient = context.Patient;

            if (patient == null)
            {
                MessageBox.Show("Please select a patient");

            }
            else
            {

                // Get plan setup id if available.
                PlanSetup planSetup = context.PlanSetup;

                string planSetupId = "";
                string planSetupUID = "";

                if (planSetup != null)
                {
                    planSetupId = context.PlanSetup.Id;
                    planSetupUID = context.PlanSetup.UID;

                }
                // Get the current user's username.
                string username = Environment.UserName;

                // Call the external executable with the additional username parameter.
                Preprocessing(context.Patient.Id, planSetupId, planSetupUID, username);
            }
        }


        public static void Preprocessing(string patientId, string planSetupId, string planSetupUID, string username)
        {
            Dictionary<string, string> config;
            string configPath;
            if (!TryLoadConfig(out config, out configPath))
            {
                MessageBox.Show(
                    "Missing launcher configuration.\n\n" +
                    "Create " + ConfigFileName + " beside this Eclipse launcher script and set:\n" +
                    "ExecutablePath=...\n" +
                    "WorkingDirectory=...\n\n" +
                    "Expected location:\n" + configPath,
                    "ARTEMIS preprocessing launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string executablePath = GetRequiredValue(config, "ExecutablePath");
            string workingDirectory = GetRequiredValue(config, "WorkingDirectory");

            if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(workingDirectory))
            {
                MessageBox.Show(
                    "Invalid launcher configuration.\n\n" +
                    ConfigFileName + " must define both ExecutablePath and WorkingDirectory.",
                    "ARTEMIS preprocessing launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string args = string.Format("{0} \"{1}\" \"{2}\" \"{3}\"",
                                patientId.Trim(),
                                planSetupId.Trim(),
                                planSetupUID.Trim(),
                                username.Trim());


            // Run process with argument patient id.
            Process proc = new Process();
            proc.StartInfo.UseShellExecute = true;

            // Do not create cmd window.
            proc.StartInfo.CreateNoWindow = true;

            proc.StartInfo.FileName = executablePath;
            proc.StartInfo.WorkingDirectory = workingDirectory;
            proc.StartInfo.Arguments = args;

            proc.Start();
        }

        private static bool TryLoadConfig(out Dictionary<string, string> config, out string configPath)
        {
            config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> candidates = GetConfigCandidates();

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                foreach (string rawLine in File.ReadAllLines(candidate))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    config[key] = value;
                }

                configPath = candidate;
                return true;
            }

            configPath = string.Join("\n", candidates.ToArray());
            return false;
        }

        private static List<string> GetConfigCandidates()
        {
            List<string> candidates = new List<string>();

            string assemblyLocation = typeof(Script).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(assemblyLocation))
            {
                string assemblyFolder = Path.GetDirectoryName(assemblyLocation);
                if (!string.IsNullOrWhiteSpace(assemblyFolder))
                {
                    candidates.Add(Path.Combine(assemblyFolder, ConfigFileName));
                }
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                candidates.Add(Path.Combine(baseDirectory, ConfigFileName));
            }

            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName));
            return candidates;
        }

        private static string GetRequiredValue(Dictionary<string, string> config, string key)
        {
            string value;
            if (config.TryGetValue(key, out value))
            {
                return value;
            }

            return "";
        }
    }
}
