using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace USZ_ARTEMIS.Configuration
{
    internal static class AppPaths
    {
        private const string ConfigFileName = "AppPaths.local.json";

        private static readonly ConfigurationLoadResult Configuration = LoadValues();
        private static readonly Dictionary<string, string> Values = Configuration.Values;

        public static string ConfigurationSourcePath
        {
            get { return Configuration.SourcePath; }
        }

        public static string ConfigurationLoadError
        {
            get { return Configuration.ErrorMessage; }
        }

        public static string PublishedScriptIconPath
        {
            get { return GetValue("PublishedScriptIconPath", @"\\YOUR-SERVER\YOUR-SHARE\ARTEMIS\PublishedScripts\Images\usz32.ico"); }
        }

        public static string RulesFolder
        {
            get { return GetValue("RulesFolder", @"\\YOUR-SERVER\YOUR-SHARE\ARTEMIS\Rules\"); }
        }

        public static string ReportsPdfFolder
        {
            get { return GetValue("ReportsPdfFolder", @"\\YOUR-SERVER\YOUR-SHARE\ARTEMIS\ReportsPDF\"); }
        }

        public static string SciMoCaExecutablePath
        {
            get { return GetValue("SciMoCaExecutablePath", @"\\YOUR-SERVER\YOUR-SHARE\ARTEMIS\Utilities\SciMoCa\SciMoCa.exe"); }
        }

        public static string SciMoCaWorkingDirectory
        {
            get
            {
                string configured = GetValue("SciMoCaWorkingDirectory", "");
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured;
                }

                return Path.GetDirectoryName(SciMoCaExecutablePath);
            }
        }

        public static string RegistrationCheckExecutablePath
        {
            get { return GetValue("RegistrationCheckExecutablePath", @"\\YOUR-SERVER\YOUR-SHARE\ARTEMIS\Utilities\RegistrationCheck\RegistrationCheck.exe"); }
        }

        private static string GetValue(string key, string defaultValue)
        {
            string value;
            if (Values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return defaultValue;
        }

        private static ConfigurationLoadResult LoadValues()
        {
            var candidates = new List<string>(GetConfigCandidates());
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    JObject json = JObject.Parse(File.ReadAllText(candidate));
                    Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JProperty property in json.Properties())
                    {
                        values[property.Name] = property.Value.ToString();
                    }

                    return new ConfigurationLoadResult(values, candidate, null);
                }
                catch (Exception ex)
                {
                    return new ConfigurationLoadResult(
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        candidate,
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            return new ConfigurationLoadResult(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                null,
                "No AppPaths.local.json was found. Checked: " + string.Join("; ", candidates));
        }

        private sealed class ConfigurationLoadResult
        {
            public ConfigurationLoadResult(
                Dictionary<string, string> values,
                string sourcePath,
                string errorMessage)
            {
                Values = values;
                SourcePath = sourcePath;
                ErrorMessage = errorMessage;
            }

            public Dictionary<string, string> Values { get; }
            public string SourcePath { get; }
            public string ErrorMessage { get; }
        }

        private static IEnumerable<string> GetConfigCandidates()
        {
            string explicitPath = Environment.GetEnvironmentVariable("USZ_ARTEMIS_APP_PATHS");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                yield return explicitPath;
            }

            string assemblyLocation = typeof(AppPaths).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(assemblyLocation))
            {
                string assemblyFolder = Path.GetDirectoryName(assemblyLocation);
                if (!string.IsNullOrWhiteSpace(assemblyFolder))
                {
                    yield return Path.Combine(assemblyFolder, ConfigFileName);
                }
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                yield return Path.Combine(baseDirectory, ConfigFileName);
            }

            yield return Path.Combine(Directory.GetCurrentDirectory(), "Configuration", ConfigFileName);
        }
    }
}
