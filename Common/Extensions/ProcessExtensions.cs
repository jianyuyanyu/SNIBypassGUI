using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.Extensions
{
    public static class ProcessExtensions
    {
        /// <summary>
        /// Asynchronously waits for the process to exit.
        /// (Implementation for .NET Framework 4.7.2 where this is missing)
        /// </summary>
        /// <param name="process">The process to wait for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that represents the asynchronous wait operation.</returns>
        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            if (process.HasExited) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;

            void ProcessExited(object sender, EventArgs e)
            {
                tcs.TrySetResult(null);
            }

            process.Exited += ProcessExited;

            if (process.HasExited)
            {
                process.Exited -= ProcessExited;
                tcs.TrySetResult(null);
            }

            // Handle cancellation
            if (cancellationToken != default)
            {
                cancellationToken.Register(() => tcs.TrySetCanceled());
            }

            return tcs.Task;
        }

        /// <summary>
        /// Asynchronously waits for the process to exit with a timeout.
        /// </summary>
        public static async Task<bool> WaitForExitAsync(this Process process, int timeoutMilliseconds)
        {
            using var cts = new CancellationTokenSource(timeoutMilliseconds);
            try
            {
                await WaitForExitAsync(process, cts.Token);
                return true;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Synchronously waits for the process to be ready/initialized.
        /// (Kept as extension because it acts ON a process instance)
        /// </summary>
        public static void WaitForReady(this Process process, int timeout = 5000, int sleepIntervalMs = 100)
        {
            try
            {
                if (process == null || process.HasExited) return;

                // Logic for background/hidden processes
                if (!process.StartInfo.UseShellExecute && process.StartInfo.CreateNoWindow)
                {
                    int waited = 0;
                    while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero && waited < timeout)
                    {
                        Thread.Sleep(sleepIntervalMs);
                        waited += sleepIntervalMs;
                    }

                    if (process.MainWindowHandle != IntPtr.Zero)
                        WriteLog($"Process {process.ProcessName} is ready.", LogLevel.Info);
                    else
                        WriteLog($"Timeout waiting for process {process.ProcessName} to be ready.", LogLevel.Warning);
                }
                else
                {
                    // Logic for GUI processes
                    if (!process.WaitForInputIdle(timeout))
                        WriteLog($"Process {process.ProcessName} did not enter idle state (might be a console app).", LogLevel.Warning);
                    else
                        WriteLog($"Process {process.ProcessName} has entered idle state.", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while waiting for process {process.ProcessName} to initialize.", LogLevel.Error, ex);
            }
        }
    }
}
