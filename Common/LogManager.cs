using System;
using System.IO;
using System.Runtime.CompilerServices;
using static SNIBypassGUI.Common.IO.FileUtils;
using static SNIBypassGUI.Consts.AppConsts;
using static SNIBypassGUI.Consts.PathConsts;

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
            AppendToFile(GetLogPath(), LogHead);
        }

        public static void DisableLog() => outputLog = false;

        public static string GetLogPath() => Path.Combine(LogDirectory, $"SNIBypassGUI-{DateTime.Now:yyyy-MM-dd}.log");

        public static void WriteLog(string message, LogLevel logLevel = LogLevel.Info, Exception ex = null, [CallerMemberName] string caller = "")
        {
            if (!outputLog || string.IsNullOrEmpty(GetLogPath()) || logLevel > currentLogLevel) return;
            lock (lockObject)
            {
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] [{caller}] {message}";
                if (ex != null) logMessage += $" | 发生错误。\n{ex.Message} | 调用堆栈：{ex.StackTrace}";
                logMessage += $"{Environment.NewLine}";
                AppendToFile(GetLogPath(), logMessage);
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
