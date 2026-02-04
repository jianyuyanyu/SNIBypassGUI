using System;
using System.IO;
using System.Runtime.CompilerServices;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Consts;

namespace SNIBypassGUI.Common
{
    public static class LogManager
    {
        private static readonly object lockObject = new();
        private static bool outputLog = false;
        private static readonly LogLevel currentLogLevel = LogLevel.Debug;

        public static bool IsLogEnabled => outputLog;

        public static void EnableLog()
        {
            outputLog = true;
            FileUtils.AppendToFile(GetLogPath(), AppConsts.LogHead);
        }

        public static void DisableLog() => outputLog = false;

        public static string GetLogPath() => Path.Combine(PathConsts.LogDirectory, $"SNIBypassGUI-{DateTime.Now:yyyy-MM-dd}.log");

        public static void WriteLog(string message, LogLevel logLevel = LogLevel.Info, Exception ex = null, [CallerMemberName] string caller = "")
        {
            if (!outputLog || string.IsNullOrEmpty(GetLogPath()) || logLevel > currentLogLevel) return;
            lock (lockObject)
            {
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] [{caller}] {message}";
                if (ex != null) logMessage += $" | Exception: {ex.Message} | StackTrace: {ex.StackTrace}";
                logMessage += $"{Environment.NewLine}";
                FileUtils.AppendToFile(GetLogPath(), logMessage);
            }
        }

        public enum LogLevel
        {
            Error,
            Warning,
            Info,
            Debug
        }
    }
}
