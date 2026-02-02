using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SNIBypassGUI.Common.Extensions;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Common.System;
using SNIBypassGUI.Consts;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.Tools
{
    public static class TailUtils
    {
        /// <summary>
        /// Stops all tracking processes for a specified file, or all tracking processes if path is empty.
        /// (Renamed from StopTailProcesses)
        /// </summary>
        /// <param name="filePath">The path of the file to stop monitoring. Stops all if empty.</param>
        /// <returns>Count of stopped processes.</returns>
        public static async Task<int> StopTracking(string filePath = "")
        {
            int stoppedCount = 0;
            try
            {
                await Task.Run(async () =>
                {
                    // Get all processes named "tail" (ignoring extension)
                    Process[] tailProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(PathConsts.TailExe));

                    foreach (var process in tailProcesses)
                    {
                        try
                        {
                            string commandLine = ProcessUtils.GetCommandLine(process);
                            if (string.IsNullOrEmpty(filePath) || commandLine.Contains(filePath, StringComparison.OrdinalIgnoreCase))
                            {
                                process.Kill();
                                await process.WaitForExitAsync(3000);

                                stoppedCount++;

                                string target = string.IsNullOrEmpty(filePath) ? "all" : $"file {filePath}";
                                WriteLog($"Successfully terminated tracking process for {target}, PID: {process.Id}.", LogLevel.Info);
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"Exception while terminating tail process PID {process.Id}.", LogLevel.Error, ex);
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                });

                if (stoppedCount == 0)
                    WriteLog($"No tracking processes found for {(string.IsNullOrEmpty(filePath) ? "any file" : filePath)}.", LogLevel.Info);
                else
                    WriteLog($"Terminated {stoppedCount} tracking processes.", LogLevel.Info);

                return stoppedCount;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while stopping tail processes.", LogLevel.Error, ex);
                return 0;
            }
        }

        /// <summary>
        /// Starts tracking file changes in real-time using 'tail.exe'.
        /// (Renamed from TailFile)
        /// </summary>
        /// <param name="filePath">Path to the file.</param>
        /// <param name="title">Window title prefix.</param>
        /// <param name="waitForStart">Whether to wait for the process window to appear.</param>
        public static Process StartTracking(string filePath, string title = "TailTracking", bool waitForStart = false)
        {
            try
            {
                FileUtils.EnsureFileExists(filePath);

                if (!File.Exists(PathConsts.TailExe))
                    FileUtils.ExtractResourceToFile(Properties.Resources.tail, PathConsts.TailExe);

                FileUtils.EnsureDirectoryExists(PathConsts.TempDirectory);

                string uniqueId = Guid.NewGuid().ToString("N");
                string expectedTitle = $"{title}_{uniqueId}";

                string tempBatchFile = Path.Combine(PathConsts.TempDirectory, $"run_tail_{uniqueId}.bat");

                StringBuilder batchContent = new();
                batchContent.AppendLine("@echo off");
                batchContent.AppendLine($"set \"expectedTitle={expectedTitle}\"");
                batchContent.AppendLine($"set \"TailExePath={PathConsts.TailExe}\"");
                batchContent.AppendLine($"set \"filePath={filePath}\"");
                batchContent.AppendLine("chcp 65001>nul");
                batchContent.AppendLine("title %expectedTitle%");
                batchContent.AppendLine("\"%TailExePath%\" -f -m 0 \"%filePath%\"");

                // Using Encoding.GetEncoding(936) (GBK) for CMD compatibility as originally requested
                File.WriteAllText(tempBatchFile, batchContent.ToString(), Encoding.GetEncoding(936));

                ProcessUtils.StartProcess(tempBatchFile, useShellExecute: true, createNoWindow: false);
                WriteLog($"Started tracking batch file {tempBatchFile}.", LogLevel.Info);

                if (waitForStart) return WaitForTrackingWindow(expectedTitle);

                return null;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while starting tracking for {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        /// <summary>
        /// Internal helper to find the specific tail process by its window title.
        /// </summary>
        private static Process WaitForTrackingWindow(string expectedTitle, int timeout = 5000)
        {
            int waited = 0;
            while (waited < timeout)
            {
                var tails = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(PathConsts.TailExe));
                foreach (var proc in tails)
                {
                    if (!string.IsNullOrEmpty(proc.MainWindowTitle) && proc.MainWindowTitle.Contains(expectedTitle))
                        return proc;
                    proc.Dispose();
                }
                Thread.Sleep(50);
                waited += 50;
            }
            WriteLog("Timeout waiting for tracking window to appear.", LogLevel.Warning);
            return null;
        }
    }
}
