using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SNIBypassGUI.Common.System;
using SNIBypassGUI.Common.Tools;
using SNIBypassGUI.Consts;
using SNIBypassGUI.Services; 
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI
{
    public partial class App : Application
    {
        /// <summary>
        /// Constructor. Initializes global exception handling and logging.
        /// </summary>
        public App()
        {
            if (!IsDotNet472OrHigherInstalled())
            {
                if (MessageBox.Show("此应用程序需要 .NET Framework 4.7.2 或更高版本。\n是否需要打开 Microsoft 官方下载页面？", "缺少必要组件", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    ProcessUtils.StartProcess(LinksConsts.Net472DownloadUrl, useShellExecute: true);
                Environment.Exit(1);
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Load configuration synchronously at startup to ensure settings are available immediately.
            ConfigManager.Instance.LoadAsync().GetAwaiter().GetResult();

            if (ConfigManager.Instance.Settings.Advanced.GUIDebug)
            {
                // Get log path
                string logPath = GetLogPath();

                // Start tracking log file
                TailUtils.StartTracking(logPath, "GUIDebug", true);

                // Enable logging
                EnableLog();
            }
        }

        /// <summary>
        /// Checks if .NET Framework 4.7.2 or higher is installed.
        /// </summary>
        private bool IsDotNet472OrHigherInstalled()
        {
            const string registryKeyPath = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";
            const int RequiredReleaseKey = 461808;

            using RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKeyPath);
            if (key != null)
            {
                object releaseValue = key.GetValue("Release");
                if (releaseValue != null && (int)releaseValue >= RequiredReleaseKey) return true;
            }
            return false;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteLog("Unhandled Dispatcher Exception!", LogLevel.Error, e.Exception);
            MessageBox.Show($"遇到未处理的异常：{e.Exception}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = (Exception)e.ExceptionObject;
            WriteLog("Unhandled Domain Exception!", LogLevel.Error, ex);
            MessageBox.Show($"遇到未处理的时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
