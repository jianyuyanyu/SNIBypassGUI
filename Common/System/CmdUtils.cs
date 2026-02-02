using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using SNIBypassGUI.Common.Extensions;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.System
{
    public static class CmdUtils
    {
        /// <summary>
        /// Executes a specified CMD command asynchronously.
        /// </summary>
        public static async Task<(bool Success, string Output, string Error)> RunCommand(string command, string workingDirectory = "", int timeoutMilliseconds = 15000)
        {
            if (string.IsNullOrWhiteSpace(command))
                return (false, "", "Command cannot be empty.");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            using var process = new Process { StartInfo = processStartInfo };

            StringBuilder output = new();
            StringBuilder error = new();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = await process.WaitForExitAsync(timeoutMilliseconds);
                if (!exited)
                {
                    // Handle Timeout
                    try
                    {
                        if (!process.HasExited) process.Kill();
                    }
                    catch
                    { /* Ignore */ }

                    WriteLog($"Execution of command '{command}' timed out.", LogLevel.Warning);
                    return (false, output.ToString(), "Process execution timed out.");
                }

                // CRITICAL: Even if exited is true, async output streams might still be flushing.
                // process.WaitForExit() (without args) blocks until the output streams are fully drained.
                // We wrap it in Task.Run to keep the method async-friendly.
                await Task.Run(() => process.WaitForExit());

                return (process.ExitCode == 0, output.ToString(), error.ToString());
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while executing command '{command}'.", LogLevel.Error, ex);
                return (false, output.ToString(), ex.Message);
            }
        }
    }
}
