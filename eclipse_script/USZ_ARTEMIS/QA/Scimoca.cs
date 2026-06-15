using System.Diagnostics;
using USZ_ARTEMIS.Configuration;

namespace USZ_ARTEMIS.QA
{
    internal class Scimoca
    {
        public static void SendToScimoca(string patientId, string planSetupId, string planSetupUID, string planDoseUID, string username)
        {
            string args = string.Format("{0} \"{1}\" \"{2}\" \"{3}\" \"{4}\"",
                    patientId.Trim(),
                    planSetupId.Trim(),
                    planSetupUID.Trim(),
                    planDoseUID.Trim(),
                    username.Trim());

            Process proc = new Process();
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.FileName = AppPaths.SciMoCaExecutablePath;
            proc.StartInfo.WorkingDirectory = AppPaths.SciMoCaWorkingDirectory;
            proc.StartInfo.Arguments = args;

            proc.Start();
        }
    }
}
