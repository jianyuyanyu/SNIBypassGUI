using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SNIBypassGUI.Common.Extensions;
using SNIBypassGUI.Common.Interop;
using SNIBypassGUI.Common.System;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.Network
{
    /// <summary>
    /// Provides utilities for managing network ports and associated processes using WinAPI.
    /// </summary>
    public static class PortUtils
    {
        /// <summary>
        /// Checks if a specific TCP port is in use (checks both IPv4 and IPv6).
        /// </summary>
        /// <param name="port">The port number to check.</param>
        /// <returns>True if the port is occupied; otherwise, false.</returns>
        public static bool IsTcpPortInUse(int port)
        {
            return GetPidByTcpPort(port, Iphlpapi.AF_INET) != 0 ||
                   GetPidByTcpPort(port, Iphlpapi.AF_INET6) != 0;
        }

        /// <summary>
        /// Gets the PID associated with a TCP port. Prioritizes IPv4 check, then IPv6.
        /// </summary>
        /// <returns>The PID if found; otherwise, 0.</returns>
        public static int FindPidForPort(int port)
        {
            int pid = GetPidByTcpPort(port, Iphlpapi.AF_INET);
            if (pid != 0) return pid;

            return GetPidByTcpPort(port, Iphlpapi.AF_INET6);
        }

        /// <summary>
        /// Asynchronously frees the specified TCP ports.
        /// Handles System process (IIS) and normal processes differently.
        /// </summary>
        public static async Task FreeTcpPortsAsync(IEnumerable<int> ports)
        {
            foreach (var port in ports)
            {
                try
                {
                    int pid = FindPidForPort(port);
                    if (pid == 0) continue;
                    if (pid == 4)
                    {
                        WriteLog($"Port {port} is occupied by System (PID 4). Attempting to stop IIS (W3SVC)...", LogLevel.Warning);

                        bool stopped = await Task.Run(() => ServiceUtils.StopService("W3SVC"));
                        if (stopped)
                            WriteLog($"Successfully stopped W3SVC to free port {port}.", LogLevel.Info);
                        else
                            WriteLog($"Failed to stop W3SVC. Port {port} might still be in use.", LogLevel.Warning);

                        continue;
                    }

                    Process process = null;
                    try
                    {
                        process = Process.GetProcessById(pid);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (process.Id == Process.GetCurrentProcess().Id) continue;

                    WriteLog($"Killing process {process.ProcessName} (PID: {pid}) to free port {port}...", LogLevel.Info);

                    try
                    {
                        process.Kill();

                        bool exited = await process.WaitForExitAsync(3000);
                        if (exited)
                            WriteLog($"Process {pid} exited successfully.", LogLevel.Info);
                        else
                            WriteLog($"Timeout waiting for process {pid} to exit.", LogLevel.Warning);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Failed to kill/wait for PID {pid}: {ex.Message}", LogLevel.Error);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Error processing port {port}: {ex.Message}", LogLevel.Error);
                }
            }
        }
        
        /// <summary>
        /// Internal helper to get PID from specific IP table version.
        /// </summary>
        private static int GetPidByTcpPort(int port, int ipVersion)
        {
            IntPtr buffer = IntPtr.Zero;
            int size = 0;

            try
            {
                var result = Iphlpapi.GetExtendedTcpTable(IntPtr.Zero, ref size, true, ipVersion, Iphlpapi.TCP_TABLE_OWNER_PID_ALL, 0);
                buffer = Marshal.AllocHGlobal(size);

                result = Iphlpapi.GetExtendedTcpTable(buffer, ref size, true, ipVersion, Iphlpapi.TCP_TABLE_OWNER_PID_ALL, 0);

                if (result != 0) return 0;

                int numEntries = Marshal.ReadInt32(buffer);
                IntPtr rowPtr = IntPtr.Add(buffer, 4);

                for (int i = 0; i < numEntries; i++)
                {
                    if (ipVersion == Iphlpapi.AF_INET)
                    {
                        var row = Marshal.PtrToStructure<Iphlpapi.MIB_TCPROW_OWNER_PID>(rowPtr);
                        if (row.LocalPort == port) return row.dwOwningPid;
                        rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf(typeof(Iphlpapi.MIB_TCPROW_OWNER_PID)));
                    }
                    else if (ipVersion == Iphlpapi.AF_INET6)
                    {
                        var row = Marshal.PtrToStructure<Iphlpapi.MIB_TCP6ROW_OWNER_PID>(rowPtr);
                        if (row.LocalPort == port) return row.dwOwningPid;
                        rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf(typeof(Iphlpapi.MIB_TCP6ROW_OWNER_PID)));
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                WriteLog($"Error querying TCP table (AF={ipVersion}): {ex.Message}", LogLevel.Error, ex);
                return 0;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
