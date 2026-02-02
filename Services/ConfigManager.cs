using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Consts;
using SNIBypassGUI.Models;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Services
{
    /// <summary>
    /// Manages application configuration using JSON with asynchronous I/O.
    /// </summary>
    public class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance = new(() => new ConfigManager());
        public static ConfigManager Instance => _instance.Value;

        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly System.Timers.Timer _debounceTimer;
        private volatile bool _isDirty = false; // Marked volatile for thread safety

        public AppConfig Settings { get; set; } // Changed to public set for loading flexibility

        private ConfigManager()
        {
            // Debounce writes to avoid disk thrashing on rapid UI toggles
            _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
            _debounceTimer.Elapsed += async (s, e) => await SaveToDiskAsync();

            // Initialize with defaults temporarily
            Settings = new AppConfig();
        }

        /// <summary>
        /// Loads configuration from disk asynchronously.
        /// </summary>
        public async Task LoadAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                if (File.Exists(PathConsts.ConfigJson))
                {
                    string json = await FileUtils.ReadAllTextAsync(PathConsts.ConfigJson);
                    Settings = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    Settings = new AppConfig();
                    // Don't save immediately here, let the first Save() call handle file creation
                }
            }
            catch (Exception ex)
            {
                WriteLog("Failed to load configuration. Using defaults.", LogLevel.Error, ex);
                Settings = new AppConfig();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Queues a save operation. This is non-blocking and debounced.
        /// Call this whenever you modify Settings.
        /// </summary>
        public void Save()
        {
            _isDirty = true;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        /// <summary>
        /// Forces an immediate save (e.g., on application exit or restart).
        /// This bypasses the debounce timer and forces a write.
        /// </summary>
        public async Task SaveNowAsync()
        {
            _isDirty = true;
            _debounceTimer.Stop();
            await SaveToDiskAsync();
        }

        private async Task SaveToDiskAsync()
        {
            if (!_isDirty) return;

            await _fileLock.WaitAsync();
            try
            {
                if (!_isDirty) return;

                await SaveToDiskInternalAsync();
                _isDirty = false;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task SaveToDiskInternalAsync()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);

                string dir = Path.GetDirectoryName(PathConsts.ConfigJson);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

                await FileUtils.WriteAllTextAsync(PathConsts.ConfigJson, json);
            }
            catch (Exception ex)
            {
                WriteLog("Failed to save configuration.", LogLevel.Error, ex);
            }
        }
    }
}