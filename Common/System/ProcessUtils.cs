using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.System
{
    public static class ProcessUtils
    {
        /// <summary>
        /// Checks if a process is currently running.
        /// </summary>
        /// <param name="processName">The name of the process.</param>
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                return GetProcessCount(processName) > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while checking if process {processName} is running.", LogLevel.Error, ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the number of running instances of a specific process.
        /// </summary>
        /// <param name="processName">The name of the process.</param>
        /// <returns>The number of processes found.</returns>
        public static int GetProcessCount(string processName)
        {
            try
            {
                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    processName = Path.GetFileNameWithoutExtension(processName);
                }

                // Get processes and ensure we dispose of the handles to prevent leaks
                Process[] processes = Process.GetProcessesByName(processName);
                int count = processes.Length;

                // Although strictly not always necessary for simple checks, 
                // it is good practice to dispose Process objects if we are not using them.
                foreach (var p in processes) p.Dispose();

                return count;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while getting count for process {processName}.", LogLevel.Error, ex);
                return -1;
            }
        }

        /// <summary>
        /// Retrieves the command line arguments of a specific process using WMI.
        /// </summary>
        /// <param name="process">The process to inspect.</param>
        /// <returns>The command line string, or empty string if failed.</returns>
        public static string GetCommandLine(Process process)
        {
            if (process == null) return string.Empty;

            try
            {
                using ManagementObjectSearcher searcher = new($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                using ManagementObjectCollection collection = searcher.Get();
                using ManagementObject obj = collection.Cast<ManagementObject>().FirstOrDefault();

                return obj?["CommandLine"]?.ToString() ?? string.Empty;
            }
            catch
            {
                // WMI queries might fail if permissions are insufficient or process exits
                return string.Empty;
            }
        }

        /// <summary>
        /// Starts a new process.
        /// </summary>
        /// <param name="fileName">The path to the executable.</param>
        /// <param name="arguments">The arguments to pass to the process.</param>
        /// <param name="workingDirectory">The working directory for the process.</param>
        /// <param name="useShellExecute">Whether to use the operating system shell to start the process.</param>
        /// <param name="createNoWindow">Whether to start the process in a new window.</param>
        public static Process StartProcess(string fileName, string arguments = "", string workingDirectory = "", bool useShellExecute = false, bool createNoWindow = false)
        {
            try
            {
                Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = useShellExecute,
                        CreateNoWindow = createNoWindow,
                        WorkingDirectory = workingDirectory
                    }
                };
                process.Start();
                WriteLog($"Successfully started {fileName}.", LogLevel.Info);
                return process;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while starting {fileName}.", LogLevel.Error, ex);
                throw;
            }
        }

        /// <summary>
        /// Kills a process by its name.
        /// </summary>
        /// <param name="processName">The name of the process.</param>
        /// <returns>True if all instances were successfully terminated; otherwise, false.</returns>
        public static bool KillProcess(string processName)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    WriteLog($"Process named {processName} not found.", LogLevel.Warning);
                    return false;
                }

                bool allKilled = true;
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000); // Give it a second to die gracefully
                        WriteLog($"Successfully killed process {processName} (PID: {process.Id}).", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Exception occurred while killing process {processName} (PID: {process.Id}).", LogLevel.Error, ex);
                        allKilled = false;
                    }
                    finally
                    {
                        process.Dispose(); // Always clean up
                    }
                }
                return allKilled;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while attempting to kill process {processName}.", LogLevel.Error, ex);
                throw;
            }
        }
    }
}
