using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace USZ_ARTEMIS.Configuration
{
    internal static class AppPaths
    {
        private const string ConfigFileName = "AppPaths.local.json";

        private static readonly Dictionary<string, string> Values = LoadValues();

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

        private static Dictionary<string, string> LoadValues()
        {
            foreach (string candidate in GetConfigCandidates())
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

                    return values;
                }
                catch
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
