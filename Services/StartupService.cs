using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32.TaskScheduler;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Common.System;
using SNIBypassGUI.Common.Text;
using SNIBypassGUI.Consts;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Services
{
    public class StartupService
    {
        /// <summary>
        /// Checks for single instance and handles parent process waiting logic.
        /// </summary>
        public void CheckSingleInstance()
        {
            string[] args = Environment.GetCommandLineArgs();

            if (ArgumentUtils.TryGetArgumentValue(args, AppConsts.WaitForParentArgument, out string parentPidStr) &&
                int.TryParse(parentPidStr, out int pid))
            {
                try
                {
                    using Process parentProcess = Process.GetProcessById(pid);
                    if (parentProcess.WaitForExit(5000))
                        WriteLog("Old process exited safely.", LogLevel.Debug);
                    else
                        WriteLog("Timeout waiting for old process, forcing startup.", LogLevel.Warning);
                }
                catch
                {
                    // Process likely already exited.
                }
            }

            if (!ArgumentUtils.ContainsArgument(args, AppConsts.IgnoreExistingArgument) &&
                ProcessUtils.GetProcessCount(Process.GetCurrentProcess().MainModule.ModuleName) > 1)
            {
                WriteLog("Program is already running. Exiting.", LogLevel.Warning);
                HandyControl.Controls.MessageBox.Show("程序已在运行中，请检查系统托盘图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Initializes necessary directories, extracts resources, and migrates legacy config.
        /// </summary>
        public void InitializeDirectoriesAndFiles()
        {
            FileUtils.TryDelete(PathConsts.OldVersionExe, 5, 500);

            foreach (string directory in PathConsts.NecessaryDirectories)
                FileUtils.EnsureDirectoryExists(directory);

            foreach (var pair in CollectionConsts.FileResourceMap)
            {
                if (!File.Exists(pair.Key))
                {
                    WriteLog($"Extracting resource: {pair.Key}", LogLevel.Info);
                    FileUtils.ExtractResourceToFile(pair.Value, pair.Key);
                }
            }

            // Note: Background initialization relies on the Config being loaded
            InitializeBackgroundImages();
        }

        public void EnsureTaskScheduler()
        {
            try
            {
                using TaskService ts = new();
                var existingTask = ts.GetTask(AppConsts.TaskName);
                bool needsRepair = true;

                if (existingTask != null)
                {
                    foreach (var action in existingTask.Definition.Actions)
                    {
                        if (action is ExecAction execAction)
                        {
                            bool isPathValid = string.Equals(execAction.Path, PathConsts.CurrentExe, StringComparison.OrdinalIgnoreCase);
                            bool hasValidArg = execAction.Arguments.IndexOf(AppConsts.AutoStartArgument, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                execAction.Arguments.IndexOf(AppConsts.CleanUpArgument, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (isPathValid && hasValidArg)
                            {
                                needsRepair = false;
                                break;
                            }
                        }
                    }
                }

                if (needsRepair)
                {
                    WriteLog("Task Scheduler entry missing or invalid. Creating default CleanUp task...", LogLevel.Info);
                    CreateTask(AppConsts.TaskName, "开机启动 SNIBypassGUI 并自动清理。", "SNIBypassGUI", PathConsts.CurrentExe, AppConsts.CleanUpArgument);
                }
            }
            catch (Exception ex)
            {
                WriteLog("Failed to verify Task Scheduler entry.", LogLevel.Error, ex);
            }
        }

        public void CreateTask(string taskName, string description, string author, string path, string args)
        {
            using TaskService ts = new();

            Task existingTask = ts.GetTask(taskName);
            if (existingTask != null) ts.RootFolder.DeleteTask(taskName);

            TaskDefinition td = ts.NewTask();
            td.RegistrationInfo.Description = description;
            td.RegistrationInfo.Author = author;
            td.Triggers.Add(new LogonTrigger());
            td.Actions.Add(new ExecAction(path, args, null));
            td.Principal.GroupId = @"BUILTIN\Administrators";
            td.Principal.RunLevel = TaskRunLevel.Highest;
            td.Principal.LogonType = TaskLogonType.Group;
            ts.RootFolder.RegisterTaskDefinition(taskName, td);
        }

        private void InitializeBackgroundImages()
        {
            var files = Directory.EnumerateFiles(PathConsts.BackgroundDirectory).ToList();

            if (!files.Any())
            {
                foreach (var pair in CollectionConsts.DefaultBackgroundMap)
                    FileUtils.ExtractResourceToFile(pair.Value, Path.Combine(PathConsts.BackgroundDirectory, pair.Key));
                files = [.. Directory.EnumerateFiles(PathConsts.BackgroundDirectory)];
            }

            HashSet<string> oldVersionHashes = [.. CollectionConsts.BackgroundHashesByVersion.Values.SelectMany(h => h)];
            bool foundOldVersion = false;
            List<string> foundOldHash = [];

            var imageOrder = ConfigManager.Instance.Settings.Background.ImageOrder;

            foreach (string file in files)
            {
                string fileHash = FileUtils.CalculateFileHash(file);
                if (fileHash != null && oldVersionHashes.Contains(fileHash))
                {
                    FileUtils.TryDelete(file, 5, 500);
                    foundOldVersion = true;
                    foundOldHash.Add(fileHash);
                }
            }

            imageOrder.RemoveAll(h => foundOldHash.Contains(h));

            if (foundOldVersion)
            {
                foreach (var pair in CollectionConsts.DefaultBackgroundMap)
                {
                    string targetPath = Path.Combine(PathConsts.BackgroundDirectory, pair.Key);
                    if (!File.Exists(targetPath))
                    {
                        FileUtils.ExtractResourceToFile(pair.Value, targetPath);
                        string hash = FileUtils.CalculateFileHash(targetPath);
                        if (!imageOrder.Contains(hash)) imageOrder.Add(hash);
                    }
                }
            }

            if (imageOrder.Count == 0)
            {
                var imgs = AppConsts.ImageExtensions.SelectMany(ext => Directory.GetFiles(PathConsts.BackgroundDirectory, "*" + ext));
                imageOrder.AddRange(imgs.Select(FileUtils.CalculateFileHash));
            }

            ConfigManager.Instance.Save();
        }
    }
}
