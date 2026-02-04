using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using static SNIBypassGUI.Common.Interop.Advapi32;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.System
{
    /// <summary>
    /// Provides common methods for installing, uninstalling, configuring, starting, and stopping Windows services.
    /// Note: Calling these methods requires Administrator privileges.
    /// </summary>
    public static class ServiceUtils
    {
        // Mapping error codes to readable constants for internal logic
        public const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        public const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
        public const int ERROR_SERVICE_NOT_ACTIVE = 1062;
        public const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

        /// <summary>
        /// Checks the state of a specific service.
        /// Returns:
        /// 1 = Exists and accessible
        /// 0 = Does not exist
        /// 2 = Marked for deletion
        /// -1 = Other error
        /// </summary>
        public static int CheckServiceState(string serviceName)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return -1;

            IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            int result;

            if (svc != IntPtr.Zero)
            {
                result = 1;
                CloseServiceHandle(svc);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_SERVICE_MARKED_FOR_DELETE)
                    result = 2;
                else if (error == ERROR_SERVICE_DOES_NOT_EXIST)
                    result = 0;
                else
                {
                    WriteLog($"Error code {error} occurred while checking state for service {serviceName}.", LogLevel.Debug);
                    result = -1;
                }
            }

            CloseServiceHandle(scm);
            return result;
        }

        /// <summary>
        /// Installs a service and attempts to start it immediately.
        /// </summary>
        public static bool InstallService(string serviceExePath, string serviceName, string displayName, uint startType = SERVICE_AUTO_START)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                WriteLog($"Failed to connect to SCM: {Marshal.GetLastWin32Error()}", LogLevel.Error);
                return false;
            }

            IntPtr svc = CreateService(
                scm,
                serviceName,
                displayName,
                SERVICE_ALL_ACCESS,
                SERVICE_WIN32_OWN_PROCESS,
                startType,
                SERVICE_ERROR_NORMAL,
                serviceExePath,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (svc == IntPtr.Zero)
            {
                WriteLog($"Failed to create service: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
                CloseServiceHandle(scm);
                return false;
            }

            // Attempt to start the service
            // Passing IntPtr.Zero as the last argument matching our updated WinApiUtils signature
            bool startResult = StartService(svc, 0, IntPtr.Zero);
            if (!startResult)
            {
                int err = Marshal.GetLastWin32Error();
                WriteLog($"Failed to start service after installation: {err}.", LogLevel.Error);
            }

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return true;
        }

        /// <summary>
        /// Uninstalls a service by name. Attempts to stop it first if running.
        /// </summary>
        public static bool UninstallService(string serviceName)
        {
            // Stop first
            StopService(serviceName);

            IntPtr scm = OpenSCManager(null, null, GENERIC_WRITE);
            if (scm == IntPtr.Zero)
            {
                WriteLog($"Failed to connect to SCM: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
                return false;
            }

            IntPtr svc = OpenService(scm, serviceName, DELETE);
            if (svc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    CloseServiceHandle(scm);
                    return true;
                }

                WriteLog($"Failed to open service for deletion: {err}.", LogLevel.Error);
                CloseServiceHandle(scm);
                return false;
            }

            bool result = DeleteService(svc);
            if (!result)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_MARKED_FOR_DELETE)
                    result = true; // Already marked, practically deleted
                else
                    WriteLog($"Failed to delete service: {err}.", LogLevel.Error);
            }

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return result;
        }

        /// <summary>
        /// Stops a service by name. Returns true if stopped or not running.
        /// </summary>
        public static bool StopService(string serviceName, int timeout = 30000, int sleepInterval = 300)
        {
            SERVICE_STATUS status = new();

            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                WriteLog($"Failed to connect to SCM: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
                return false;
            }

            IntPtr svc = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    CloseServiceHandle(scm);
                    return true;
                }

                WriteLog($"Failed to open service to stop: {err}.", LogLevel.Error);
                CloseServiceHandle(scm);
                return false;
            }

            // Send Stop Control
            bool controlResult = ControlService(svc, SERVICE_CONTROL_STOP, ref status);
            if (!controlResult)
            {
                int error = Marshal.GetLastWin32Error();
                // If service is not active (already stopped) or gone, consider it success
                if (error == ERROR_SERVICE_NOT_ACTIVE || error == ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    CloseServiceHandle(svc);
                    CloseServiceHandle(scm);
                    return true;
                }
                WriteLog($"Failed to send stop control: {error}.", LogLevel.Error);
            }

            // Wait for the service to stop
            int elapsed = 0;
            while (status.dwCurrentState != SERVICE_STOPPED && elapsed < timeout)
            {
                Thread.Sleep(sleepInterval);
                elapsed += sleepInterval;

                if (!QueryServiceStatus(svc, ref status))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        CloseServiceHandle(svc);
                        CloseServiceHandle(scm);
                        return true;
                    }

                    WriteLog($"Failed to query service status: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
                    break;
                }
            }

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return status.dwCurrentState == SERVICE_STOPPED;
        }

        /// <summary>
        /// Changes the start type of a specified service.
        /// </summary>
        public static bool ChangeServiceStartType(string serviceName, uint startType)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero) return false;

            IntPtr svc = OpenService(scm, serviceName, SERVICE_CHANGE_CONFIG);
            if (svc == IntPtr.Zero)
            {
                CloseServiceHandle(scm);
                return false;
            }

            bool result = ChangeServiceConfig(
                svc,
                SERVICE_NO_CHANGE,
                startType,
                SERVICE_NO_CHANGE,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null);

            if (!result)
            {
                WriteLog($"Failed to change service config: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
            }

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return result;
        }

        /// <summary>
        /// Starts a service by name.
        /// </summary>
        public static bool StartServiceByName(string serviceName)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                WriteLog($"Failed to connect to SCM: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
                return false;
            }

            IntPtr svc = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero)
            {
                WriteLog($"Failed to open service to start: {Marshal.GetLastWin32Error()}.", LogLevel.Error);
                CloseServiceHandle(scm);
                return false;
            }

            // Using IntPtr.Zero to match WinApiUtils signature
            bool result = StartService(svc, 0, IntPtr.Zero);
            if (!result)
            {
                int err = Marshal.GetLastWin32Error();
                // 1056 = ERROR_SERVICE_ALREADY_RUNNING
                if (err != ERROR_SERVICE_ALREADY_RUNNING)
                    WriteLog($"Failed to start service: {err}.", LogLevel.Error);
            }

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return result;
        }

        /// <summary>
        /// Queries the current status of a service.
        /// </summary>
        public static SERVICE_STATUS QueryServiceStatusByName(string serviceName)
        {
            SERVICE_STATUS status = new();

            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero) throw new Exception("SCM Connect Failed");

            IntPtr svc = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero)
            {
                CloseServiceHandle(scm);
                throw new Exception("Open Service Failed");
            }

            if (!QueryServiceStatus(svc, ref status))
            {
                int err = Marshal.GetLastWin32Error();
                CloseServiceHandle(svc);
                CloseServiceHandle(scm);
                throw new Exception($"Query Failed: {err}");
            }

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return status;
        }

        /// <summary>
        /// Gets the binary path of a service from Registry.
        /// Handles quotes and null values safely.
        /// </summary>
        public static string GetServiceBinaryPath(string serviceName)
        {
            try
            {
                string regPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath);

                if (key != null)
                {
                    object binaryPathValue = key.GetValue("ImagePath");
                    if (binaryPathValue != null)
                    {
                        string path = binaryPathValue.ToString();
                        return path.Trim('"');
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to get binary path for service {serviceName}.", LogLevel.Debug, ex);
            }
            return null;
        }
    }
}
