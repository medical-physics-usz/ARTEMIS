using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace USZ_ARTEMIS.Tools
{
    internal static class ExternalProcessLauncher
    {
        internal static async Task<int> RunAsync(ProcessStartInfo startInfo)
        {
            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (_, __) => exited.TrySetResult(true);
                if (!process.Start())
                    throw new InvalidOperationException("Windows did not start the configured executable.");

                await exited.Task.ConfigureAwait(false);
                return process.ExitCode;
            }
        }

        internal static bool IsAbsolutePath(string path)
        {
            // A rooted Windows path such as C:tool.exe or \tool.exe still depends
            // on Eclipse's current directory or drive.
            return Path.IsPathRooted(path) && string.Equals(
                Path.GetPathRoot(path), Path.GetPathRoot(Path.GetFullPath(path)),
                StringComparison.OrdinalIgnoreCase);
        }

        // .NET Framework lacks ArgumentList. Escape quotes and trailing backslashes
        // according to the Windows command-line rules used by the Python executable.
        internal static string QuoteArgument(string value)
        {
            var result = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
                result.Append(character);
                backslashes = 0;
            }

            result.Append('\\', backslashes * 2);
            return result.Append('"').ToString();
        }
    }
}
