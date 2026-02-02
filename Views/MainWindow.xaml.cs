using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32.TaskScheduler;
using HandyControl.Tools.Extension;
using Hardcodet.Wpf.TaskbarNotification;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SNIBypassGUI.Common;
using SNIBypassGUI.Common.Commands;
using SNIBypassGUI.Common.Extensions;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Common.Network;
using SNIBypassGUI.Common.Security;
using SNIBypassGUI.Common.System;
using SNIBypassGUI.Common.Text;
using SNIBypassGUI.Common.Tools;
using SNIBypassGUI.Common.UI;
using SNIBypassGUI.Consts;
using SNIBypassGUI.Models;
using SNIBypassGUI.Services;
using static SNIBypassGUI.Common.LogManager;
using Action = System.Action;
using MessageBox = HandyControl.Controls.MessageBox;
using Task = System.Threading.Tasks.Task;

namespace SNIBypassGUI.Views
{
    public partial class MainWindow : Window
    {
        #region Fields & Services
        private readonly StartupService _startupService = new();
        private readonly ProxyService _proxyService = new();

        // Timers
        private readonly DispatcherTimer _serviceStatusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _tempFilesTimer = new() { Interval = TimeSpan.FromSeconds(10) };
        private readonly DispatcherTimer _adapterSwitchTimer = new() { Interval = TimeSpan.FromSeconds(5) };

        // Configuration Watcher
        public static FileSystemWatcher ConfigWatcher = new()
        {
            Filter = Path.GetFileName(PathConsts.ConfigJson),
            NotifyFilter = NotifyFilters.LastWrite
        };
        private static readonly Timer _reloadDebounceTimer = new(500.0) { AutoReset = false };

        private volatile bool _isSwitchingAdapter = false;
        private bool _isBusy = false;

        public ICommand TaskbarIconLeftClickCommand { get; }
        public static ImageSwitcherService BackgroundService { get; private set; }

        #endregion

        #region Constructor & Init

        public MainWindow()
        {
            InitializeComponent();

            _startupService.CheckSingleInstance();
            _startupService.InitializeDirectoriesAndFiles();

            DataContext = this;

            BackgroundService = new ImageSwitcherService();
            BackgroundService.PropertyChanged += OnBackgroundChanged;
            CurrentImage.Source = BackgroundService.CurrentImage;

            TaskbarIconLeftClickCommand = new AsyncCommand(TaskbarIcon_LeftClick);
            TopBar.MouseLeftButtonDown += (o, e) => DragMove();

            TrayIconUtils.RefreshNotification();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Load configuration first
            await ConfigManager.Instance.LoadAsync();

            // Apply loaded config to UI controls
            ApplySettings();

            string[] cliargs = Environment.GetCommandLineArgs();

            // Cleanup Mode Check
            if (ArgumentUtils.ContainsArgument(cliargs, AppConsts.CleanUpArgument))
            {
                WriteLog("Startup in Cleanup Mode. Restoring settings and exiting...", LogLevel.Info);
                Exit(true);
                return;
            }

            TaskbarIcon.Visibility = Visibility.Visible;
            WindowTitle.Text = "SNIBypassGUI " + AppConsts.CurrentVersion;

            _serviceStatusTimer.Tick += (_, _) => UpdateServiceStatus();

            _tempFilesTimer.Tick += (_, _) => UpdateTempFilesSize();
            _tempFilesTimer.Start();

            _adapterSwitchTimer.Tick += AdapterAutoSwitchTimer_Tick;
            if (ConfigManager.Instance.Settings.Program.AutoSwitchAdapter)
                _adapterSwitchTimer.Start();

            MainTabControl.SelectionChanged += TabControl_SelectionChanged;

            // Watch for external JSON changes
            ConfigWatcher.Path = PathConsts.DataDirectory;
            ConfigWatcher.Changed += OnConfigChanged;
            ConfigWatcher.EnableRaisingEvents = true;
            _reloadDebounceTimer.Elapsed += OnReloadDebounceTimerElapsed;

            await AddSwitchesToList();
            await InitializeSwitchConfig();

            UpdateServiceStatus();

            if (!CertificateUtils.IsCertificateInstalled(AppConsts.CertificateThumbprint))
            {
                WriteLog($"Certificate {AppConsts.CertificateThumbprint} not found. Installing...", LogLevel.Info);
                CertificateUtils.InstallCertificate(PathConsts.CA);
            }

            await CheckAndInstallService();
            await InitializeAdapterSelection();

            ShowAndInitWindow(cliargs);

            _startupService.EnsureTaskScheduler();
            await UpdateYiyan();

            if (ConfigManager.Instance.Settings.Program.AutoCheckUpdate)
                await CheckUpdate();
        }

        private void ShowAndInitWindow(string[] args)
        {
            if (ArgumentUtils.ContainsArgument(args, AppConsts.AutoStartArgument))
            {
                WriteLog("Auto-start triggered via Task Scheduler. Starting minimized.", LogLevel.Info);
                _ = ExecuteStartServiceAsync(true);
            }
            else
            {
                Show();
                AnimateWindow(0.0, 1.0, () => { Activate(); ShowContent(); });
            }
            ShowInTaskbar = true;
        }

        private async Task InitializeSwitchConfig()
        {
            await Task.Run(() =>
            {
                var settings = ConfigManager.Instance.Settings.ProxySettings;
                bool changed = false;

                foreach (var item in CollectionConsts.Switches)
                {
                    if (!settings.ContainsKey(item.Id))
                    {
                        settings[item.Id] = true;
                        changed = true;
                    }
                }

                if (changed) ConfigManager.Instance.Save();
            });
        }

        #endregion

        #region Core Actions

        private async void StartBtn_Click(object sender, RoutedEventArgs e) => await ExecuteStartServiceAsync(false);
        private async void StopBtn_Click(object sender, RoutedEventArgs e) => await ExecuteStopServiceAsync(false);

        private async Task<bool> ExecuteStartServiceAsync(bool silent)
        {
            if (_isBusy) return false;

            _serviceStatusTimer.Stop(); // Pause timer to prevent status overwrite
            SetBusyState(true);

            bool success = false;

            try
            {
                if (string.IsNullOrEmpty(AdaptersCombo.SelectedItem?.ToString()))
                {
                    if (!silent) MessageBox.Show("请先在下拉框中选择当前正在使用的适配器！", "提示", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return false;
                }

                if (NetworkUtils.IsPortInUse(80, false) || NetworkUtils.IsPortInUse(443, false))
                {
                    if (!silent)
                    {
                        if (MessageBox.Show("检测到系统 80 或 443 端口被占用，主服务可能无法正常运行，但仍然会尝试继续启动。\n点击“是”将为您展示有关帮助。", "警告", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.None) == MessageBoxResult.Yes)
                            ProcessUtils.StartProcess("https://github.com/racpast/SNIBypassGUI/wiki/❓%EF%B8%8F-使用时遇到问题#当您的主服务运行后自动停止，或遇到80端口被占用的提示时", "", "", true, false);
                    }
                    else WriteLog("Ports 80/443 in use. Attempting start anyway.", LogLevel.Warning);
                }

                await _proxyService.UpdateHostsFromConfigAsync();

                await _proxyService.StartAsync(status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ServiceStatusText.Text = status;
                        ServiceStatusText.Foreground = Brushes.DarkOrange;
                    });
                });

                await Task.Run(() => NetworkUtils.FlushDNS());
                success = true;
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to start service: {ex.Message}", LogLevel.Error, ex);
                if (!silent) MessageBox.Show($"启动服务失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
                UpdateServiceStatus();
                _serviceStatusTimer.Start();
            }

            return success;
        }

        private async Task<bool> ExecuteStopServiceAsync(bool silent)
        {
            if (_isBusy) return false;

            _serviceStatusTimer.Stop();
            SetBusyState(true);
            bool success = false;

            try
            {
                await _proxyService.RemoveHostsRecordsAsync();
                await _proxyService.StopAsync(status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ServiceStatusText.Text = status;
                        ServiceStatusText.Foreground = Brushes.DarkOrange;
                    });
                });

                await Task.Run(() => NetworkUtils.FlushDNS());
                success = true;
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to stop service: {ex.Message}", LogLevel.Error, ex);
                if (!silent) MessageBox.Show($"停止服务失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
                UpdateServiceStatus();
                _serviceStatusTimer.Start();
            }

            return success;
        }

        private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyBtn.IsEnabled = UnchangeBtn.IsEnabled = false;
            ApplyBtn.Content = "应用更改中";

            bool wasRunning = AcrylicUtils.IsAcrylicServiceRunning();

            try { await Task.Run(() => AcrylicUtils.StopAcrylicService()); } catch { }

            UpdateConfigFromToggleButtons();
            ConfigManager.Instance.Save();

            await _proxyService.UpdateHostsFromConfigAsync();

            if (wasRunning) await AcrylicUtils.StartAcrylicService();

            AcrylicUtils.RemoveAcrylicCacheFile();
            await Task.Run(() => NetworkUtils.FlushDNS());

            ApplyBtn.Content = "应用更改";
        }

        private void SetBusyState(bool isBusy)
        {
            _isBusy = isBusy;
            bool enabled = !isBusy;

            StartBtn.IsEnabled = enabled;
            StopBtn.IsEnabled = enabled;
            AutoSwitchAdapterBtn.IsEnabled = enabled;
            RefreshBtn.IsEnabled = enabled;

            bool autoSwitch = ConfigManager.Instance.Settings.Program.AutoSwitchAdapter;
            AdaptersCombo.IsEnabled = enabled && !autoSwitch;
        }

        #endregion

        #region UI Updates & Helper Methods

        public void UpdateServiceStatus()
        {
            if (_isBusy) return;

            bool isNginxRunning = ProcessUtils.IsProcessRunning(AppConsts.NginxProcessName);
            bool isDnsRunning = AcrylicUtils.IsAcrylicServiceRunning();
            bool isAutoSwitch = ConfigManager.Instance.Settings.Program.AutoSwitchAdapter;

            Dispatcher.Invoke(() =>
            {
                var (text, color, btnEnabled, comboEnabled) = (isNginxRunning, isDnsRunning) switch
                {
                    (true, true) => ("主服务和DNS服务运行中", Brushes.ForestGreen, false, false),
                    (true, false) => ("仅主服务运行中", Brushes.DarkOrange, false, false),
                    (false, true) => ("仅DNS服务运行中", Brushes.DarkOrange, false, false),
                    (false, false) => ("主服务与DNS服务未运行", Brushes.Red, true, !isAutoSwitch)
                };

                ServiceStatusText.Text = TaskbarIconServiceST.Text = text;
                ServiceStatusText.Foreground = TaskbarIconServiceST.Foreground = color;

                if (!_isBusy)
                {
                    AutoSwitchAdapterBtn.IsEnabled = btnEnabled;
                    AdaptersCombo.IsEnabled = comboEnabled;
                }
            });
        }

        private void UpdateTempFilesSize()
        {
            long total = FileUtils.GetDirectorySize(PathConsts.LogDirectory) +
                         FileUtils.GetFileSize(PathConsts.NginxAccessLog) +
                         FileUtils.GetFileSize(PathConsts.NginxErrorLog) +
                         FileUtils.GetFileSize(PathConsts.AcrylicCache) +
                         FileUtils.GetDirectorySize(PathConsts.NginxCacheDirectory);

            CleanBtn.Content = $"清理临时文件 ({total.ToReadableSize()})";
        }

        private async Task UpdateYiyan()
        {
            try
            {
                string text = await NetworkUtils.GetAsync("https://v1.hitokoto.cn/?c=d", 10.0, "Mozilla/5.0");
                JObject repodata = JObject.Parse(text);
                TaskbarIconYiyan.Text = repodata["hitokoto"].ToString();
                TaskbarIconYiyanFrom.Text = $"—— {repodata["from_who"]}「{repodata["from"]}」";
            }
            catch (Exception ex)
            {
                WriteLog("Failed to fetch Hitokoto. Using default.", LogLevel.Error, ex);
                TaskbarIconYiyan.Text = AppConsts.DefaultYiyan;
                TaskbarIconYiyanFrom.Text = AppConsts.DefaultYiyanFrom;
            }
        }

        private async Task CheckAndInstallService()
        {
            try
            {
                int serviceState = ServiceUtils.CheckServiceState(AppConsts.DnsServiceName);
                if (serviceState == 0 || serviceState == 2)
                {
                    await AcrylicUtils.InstallAcrylicServiceAsync();
                }
                else if (serviceState == 1)
                {
                    string currentPath = ServiceUtils.GetServiceBinaryPath(AppConsts.DnsServiceName)?.Trim('"');
                    if (!string.IsNullOrEmpty(currentPath) && !string.Equals(currentPath, PathConsts.AcrylicServiceExe, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLog("DNS Service path changed. Reinstalling...", LogLevel.Info);
                        await AcrylicUtils.UninstallAcrylicServiceAsync();
                        await AcrylicUtils.InstallAcrylicServiceAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("Exception checking DNS service.", LogLevel.Error, ex);
                MessageBox.Show($"检查服务时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
        }

        #endregion

        #region Adapter Logic

        private async Task InitializeAdapterSelection()
        {
            if (AdaptersCombo.SelectedItem == null)
            {
                var active = await GetActiveAdapter();
                if (active != null)
                {
                    bool exists = AdaptersCombo.Items.OfType<string>().Any(item => item == active.FriendlyName);
                    if (!exists) await UpdateAdaptersCombo();

                    AdaptersCombo.SelectedItem = active.FriendlyName;
                }
                else
                {
                    if (AdaptersCombo.Items.Count == 0) await UpdateAdaptersCombo();

                    if (AdaptersCombo.Items.Count == 0)
                    {
                        WriteLog("No active network adapters found.", LogLevel.Warning);
                        if (MessageBox.Show("没有找到活动且可设置的网络适配器！您可能需要手动设置。\n点击“是”将为您展示有关帮助。", "警告", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.None) == MessageBoxResult.Yes)
                            ProcessUtils.StartProcess(LinksConsts.AdapterNotFoundOrSetupFailedHelpLink, useShellExecute: true);
                    }
                }
            }
        }

        private async Task<NetworkAdapter> GetActiveAdapter()
        {
            uint? interfaceIndex = NetworkAdapterUtils.GetDefaultRouteInterfaceIndex();
            if (interfaceIndex != null)
            {
                var adapters = await NetworkAdapterUtils.GetNetworkAdaptersAsync(NetworkAdapterUtils.ScopeNeeded.FriendlyNameNotNullOnly);
                return adapters.FirstOrDefault(a => a.InterfaceIndex == interfaceIndex.Value);
            }
            return null;
        }

        private async Task UpdateAdaptersCombo()
        {
            var adapters = await NetworkAdapterUtils.GetNetworkAdaptersAsync(NetworkAdapterUtils.ScopeNeeded.FriendlyNameNotNullOnly);
            AdaptersCombo.Items.Clear();
            foreach (var adapter in adapters)
            {
                if (!string.IsNullOrEmpty(adapter.FriendlyName))
                    AdaptersCombo.Items.Add(adapter.FriendlyName);
            }
        }

        private async void AdapterAutoSwitchTimer_Tick(object sender, EventArgs e)
        {
            if (_isSwitchingAdapter) return;
            _isSwitchingAdapter = true;

            try
            {
                var active = await GetActiveAdapter();
                if (active != null)
                {
                    string currentConfigured = ConfigManager.Instance.Settings.Program.SpecifiedAdapter;
                    if (active.FriendlyName != currentConfigured && !string.IsNullOrEmpty(active.FriendlyName))
                    {
                        WriteLog($"Adapter changed from {currentConfigured} to {active.FriendlyName}. Switching...", LogLevel.Info);

                        bool isServiceRunning = false;
                        if (!_isBusy)
                            Dispatcher.Invoke(() => isServiceRunning = !ServiceStatusText.Text.Contains("未运行"));

                        if (isServiceRunning)
                        {
                            var oldAdapter = (await NetworkAdapterUtils.GetNetworkAdaptersAsync(NetworkAdapterUtils.ScopeNeeded.All))
                                .FirstOrDefault(d => d.FriendlyName == currentConfigured);

                            if (oldAdapter != null) await _proxyService.RestoreAdapterDNSAsync(oldAdapter);
                            await _proxyService.SetLoopbackDNSAsync(active);
                            await Task.Run(() => NetworkUtils.FlushDNS());
                        }

                        ConfigManager.Instance.Settings.Program.SpecifiedAdapter = active.FriendlyName;
                        ConfigManager.Instance.Save();

                        Dispatcher.Invoke(() =>
                        {
                            bool exists = false;
                            foreach (var item in AdaptersCombo.Items) { if (item.ToString() == active.FriendlyName) { exists = true; break; } }
                            if (!exists) AdaptersCombo.Items.Add(active.FriendlyName);
                            AdaptersCombo.SelectedItem = active.FriendlyName;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Auto-switch adapter error: {ex.Message}", LogLevel.Error, ex);
            }
            finally
            {
                _isSwitchingAdapter = false;
            }
        }

        private void AdaptersCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AdaptersCombo.SelectedItem != null)
            {
                ConfigManager.Instance.Settings.Program.SpecifiedAdapter = AdaptersCombo.SelectedItem.ToString();
                ConfigManager.Instance.Save();
            }
            e.Handled = true;
        }

        private void AutoSwitchAdapterBtn_Click(object sender, RoutedEventArgs e)
        {
            bool current = ConfigManager.Instance.Settings.Program.AutoSwitchAdapter;
            bool newState = !current;

            // Immediate UI update logic
            if (newState)
            {
                if (!_adapterSwitchTimer.IsEnabled) _adapterSwitchTimer.Start();
                AdapterAutoSwitchTimer_Tick(null, new EventArgs());
            }
            else if (_adapterSwitchTimer.IsEnabled) _adapterSwitchTimer.Stop();

            // Update Config
            ConfigManager.Instance.Settings.Program.AutoSwitchAdapter = newState;
            ConfigManager.Instance.Save();

            // UI Update
            AutoSwitchAdapterBtn.Content = $"自动：{newState.ToOnOff()}";
            AdaptersCombo.IsEnabled = !newState;
        }

        #endregion

        #region Switch & Config Logic

        private async Task AddSwitchesToList()
        {
            if (!File.Exists(PathConsts.ProxyRules))
            {
                WriteLog("Switch config file not found.", LogLevel.Warning);
                return;
            }

            string json = await FileUtils.ReadAllTextAsync(PathConsts.ProxyRules);
            List<SwitchItem> switchItems = JsonConvert.DeserializeObject<List<SwitchItem>>(json);

            if (switchItems == null || switchItems.Count == 0) return;

            CollectionConsts.Switches.Clear();
            foreach (var item in switchItems)
            {
                item.FaviconImage = ImageUtils.Base64ToBitmapImage(item.Favicon);
                CollectionConsts.Switches.Add(item);
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                for (int i = Switchlist.Children.Count - 1; i >= 0; i--)
                {
                    var child = Switchlist.Children[i];
                    if (child != FirstColumnBorder && child != LastColumnBorder)
                        Switchlist.Children.RemoveAt(i);
                }

                Switchlist.RowDefinitions.Clear();

                foreach (var item in CollectionConsts.Switches) AddSwitchToUI(item);

                if (Switchlist.RowDefinitions.Count > 0)
                {
                    Grid.SetRowSpan(FirstColumnBorder, Switchlist.RowDefinitions.Count);
                    Grid.SetRowSpan(LastColumnBorder, Switchlist.RowDefinitions.Count);
                }
            });
        }
        private void AddSwitchToUI(SwitchItem item)
        {
            Switchlist.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int rowIndex = Switchlist.RowDefinitions.Count - 1;

            // 1. Favicon
            Image favicon = new()
            {
                Source = item.FaviconImage,
                Height = 32.0,
                Width = 32.0,
                Margin = new Thickness(10.0, 10.0, 10.0, 5.0)
            };
            Grid.SetRow(favicon, rowIndex);
            Grid.SetColumn(favicon, 0);
            Switchlist.Children.Add(favicon);

            // 2. Text & Links
            TextBlock textBlock = new()
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5.0, 3.0, 10.0, 3.0),
                TextWrapping = TextWrapping.Wrap
            };
            textBlock.Inlines.Add(new Run { Text = item.DisplayName, FontSize = 16.0 });
            textBlock.Inlines.Add(new LineBreak());

            foreach (string part in item.Links)
            {
                if (part.Length <= 1)
                    textBlock.Inlines.Add(new Run { Text = part, FontSize = 15.0, FontWeight = FontWeights.Bold });
                else
                {
                    Run run = new()
                    {
                        Text = part,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 249, 255)),
                        FontSize = 15.0,
                        Cursor = Cursors.Hand
                    };
                    run.PreviewMouseDown += LinkText_PreviewMouseDown;
                    textBlock.Inlines.Add(run);
                }
            }
            Grid.SetRow(textBlock, rowIndex);
            Grid.SetColumn(textBlock, 1);
            Switchlist.Children.Add(textBlock);

            // 3. ToggleButton
            string safeName = $"Toggle_{item.Id.ToSafeIdentifier()}";

            ToggleButton toggleButton = new()
            {
                Width = 40.0,
                Margin = new Thickness(5.0, 0.0, 5.0, 0.0),
                IsChecked = true,
                Style = (Style)FindResource("ToggleButtonSwitch"),
                Tag = item.Id
            };
            toggleButton.Click += ToggleButtonsClick;

            Grid.SetRow(toggleButton, rowIndex);
            Grid.SetColumn(toggleButton, 2);
            Switchlist.Children.Add(toggleButton);

            if (Switchlist.FindName(safeName) != null)
                Switchlist.UnregisterName(safeName);
            Switchlist.RegisterName(safeName, toggleButton);
        }

        private void ApplySettings()
        {
            var settings = ConfigManager.Instance.Settings;

            bool isNight = settings.Program.ThemeMode != ConfigConsts.LightMode;
            SwitchTheme(isNight);
            ThemeSwitchTB.IsChecked = isNight;

            foreach (var item in CollectionConsts.Switches)
            {
                string safeName = $"Toggle_{item.Id.ToSafeIdentifier()}";
                ToggleButton tb = (ToggleButton)Switchlist.FindName(safeName);

                if (settings.ProxySettings.TryGetValue(item.Id, out bool enabled))
                    tb?.SetCurrentValue(ToggleButton.IsCheckedProperty, enabled);
            }

            bool debugMode = settings.Advanced.DebugMode;
            DebugModeBtn.Content = $"调试模式：\n{debugMode.ToOnOff()}";

            bool guiDebug = settings.Advanced.GUIDebug;
            GUIDebugBtn.Content = $"GUI调试：\n{guiDebug.ToOnOff()}";

            bool acrylicDebug = settings.Advanced.AcrylicDebug;
            AcrylicDebugBtn.Content = $"DNS调试：\n{acrylicDebug.ToOnOff()}";

            if (!debugMode)
            {
                DisableLog();
                TailUtils.StopTracking(GetLogPath()).GetAwaiter();
                TailUtils.StopTracking(PathConsts.NginxAccessLog).GetAwaiter();
                TailUtils.StopTracking(PathConsts.NginxErrorLog).GetAwaiter();
                FileUtils.ClearFolder(PathConsts.TempDirectory, false);
            }

            if (!guiDebug) DisableLog();
            if (!acrylicDebug) AcrylicUtils.DisableAcrylicServiceHitLog();

            AcrylicDebugBtn.IsEnabled = GUIDebugBtn.IsEnabled = TraceNginxLogBtn.IsEnabled = debugMode;

            bool autoCheckUpdate = settings.Program.AutoCheckUpdate;
            AutoCheckUpdateBtn.Content = $"自动检查更新：{autoCheckUpdate.ToOnOff()}";

            bool autoSwitch = settings.Program.AutoSwitchAdapter;
            AutoSwitchAdapterBtn.Content = $"自动：{autoSwitch.ToOnOff()}";
            AdaptersCombo.IsEnabled = !autoSwitch;

            if (autoSwitch) if (!_adapterSwitchTimer.IsEnabled) _adapterSwitchTimer.Start();
            else if (_adapterSwitchTimer.IsEnabled) _adapterSwitchTimer.Stop();
        }

        private void UpdateConfigFromToggleButtons()
        {
            foreach (var item in CollectionConsts.Switches)
            {
                string safeName = $"Toggle_{item.Id.ToSafeIdentifier()}";
                ToggleButton tb = (ToggleButton)Switchlist.FindName(safeName);
                if (tb != null)
                    ConfigManager.Instance.Settings.ProxySettings[item.Id] = tb.IsChecked.GetValueOrDefault();
            }
        }

        private static void OnConfigChanged(object source, FileSystemEventArgs e)
        {
            _reloadDebounceTimer.Stop();
            _reloadDebounceTimer.Start();
        }

        private void OnReloadDebounceTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                try
                {
                    WriteLog("Configuration file changed externally, reloading...", LogLevel.Info);
                    await ConfigManager.Instance.LoadAsync();
                    ApplySettings();
                    WriteLog("Hot reload complete.", LogLevel.Info);
                }
                catch (Exception ex) { WriteLog("Error during hot reload.", LogLevel.Error, ex); }
            });
        }

        #endregion

        #region Cleanup, Uninstall & Update

        private async void CleanBtn_Click(object sender, RoutedEventArgs e)
        {
            CleanBtn.IsEnabled = false;
            _tempFilesTimer.Stop();
            CleanBtn.Content = "服务停止中…";

            await _proxyService.StopAsync(null);

            CleanBtn.Content = "清理中…";
            await Task.Run(async () =>
            {
                try
                {
                    string[] tempfiles = [PathConsts.NginxAccessLog, PathConsts.NginxErrorLog, PathConsts.AcrylicCache, AcrylicUtils.GetLogPath()];
                    foreach (string path in tempfiles)
                    {
                        await TailUtils.StopTracking(path);
                        FileUtils.TryDelete(path, 5, 500);
                    }

                    if (IsLogEnabled)
                    {
                        Dispatcher.Invoke(() => MessageBox.Show("GUI 调试开启时将不会删除调试日志，请尝试关闭 GUI 调试。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk, MessageBoxResult.None));
                        foreach (string file in Directory.GetFiles(PathConsts.LogDirectory))
                            if (file != GetLogPath()) FileUtils.TryDelete(file, 5, 500);
                    }
                    else
                    {
                        await TailUtils.StopTracking(GetLogPath());
                        FileUtils.ClearFolder(PathConsts.LogDirectory, false);
                    }
                }
                catch (Exception ex) { WriteLog("Cleanup error.", LogLevel.Error, ex); }
            });

            WriteLog("Cleanup complete.", LogLevel.Info);
            _tempFilesTimer.Start();
            MessageBox.Show("服务运行日志及缓存清理完成，请自行重启服务！", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            UpdateTempFilesSize();
            CleanBtn.IsEnabled = true;
        }

        private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("该功能将立即从系统上移除本程序并消除本程序对系统设置所作的有关修改。\n是否继续卸载？", "卸载", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None) == MessageBoxResult.Yes)
            {
                UninstallBtn.Content = "卸载中…";
                UninstallBtn.IsEnabled = false;
                try
                {
                    CertificateUtils.UninstallCertificate(AppConsts.CertificateThumbprint);
                    await ExecuteStopServiceAsync(true);

                    using (TaskService ts = new())
                        if (ts.GetTask(AppConsts.TaskName) != null) ts.RootFolder.DeleteTask(AppConsts.TaskName, true);

                    var adapters = await NetworkAdapterUtils.GetNetworkAdaptersAsync(NetworkAdapterUtils.ScopeNeeded.FriendlyNameNotNullOnly);
                    var activeAdapter = adapters.FirstOrDefault(a => a.FriendlyName == ConfigManager.Instance.Settings.Program.SpecifiedAdapter);
                    if (activeAdapter != null) await _proxyService.RestoreAdapterDNSAsync(activeAdapter);

                    await Task.Run(() => NetworkUtils.FlushDNS());
                    await TailUtils.StopTracking();
                    await AcrylicUtils.UninstallAcrylicServiceAsync();
                    BackgroundService.Cleanup();

                    FileUtils.TryDelete(PathConsts.DataDirectory, 5, 500);
                    FileUtils.TryDelete(PathConsts.TempDirectory, 5, 500);

                    string bat = $@"@echo off
                                    timeout /t 1 /nobreak >nul
                                    taskkill /f /pid {Process.GetCurrentProcess().Id} >nul 2>&1
                                    taskkill /f /im ""tail.exe"" >nul 2>&1
                                    timeout /t 1 /nobreak >nul
                                    del /f /q ""{PathConsts.CurrentExe}"" >nul 2>&1
                                    if exist ""{PathConsts.CurrentExe}"" (
                                    echo MsgBox ""卸载失败，请手动删除文件！"", 48, ""警告"" > ""%temp%\temp.vbs""
                                    cscript /nologo ""%temp%\temp.vbs"" >nul
                                    del ""%temp%\temp.vbs""
                                    ) else (
                                    echo MsgBox ""卸载成功！"", 64, ""提示"" > ""%temp%\temp.vbs""
                                    cscript /nologo ""%temp%\temp.vbs"" >nul
                                    del ""%temp%\temp.vbs""
                                    )
                                    start /b cmd /c del ""%~f0""";

                    string batPath = Path.Combine(Path.GetTempPath(), "uninstall_snibypassgui.bat");
                    await FileUtils.WriteAllTextAsync(batPath, bat, Encoding.Default);
                    Process.Start(new ProcessStartInfo { FileName = batPath, WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    Exit(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"卸载时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
                }
            }
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateBtn.IsEnabled = false;
            UpdateBtn.Content = "获取信息…";
            try
            {
                string json = await NetworkUtils.GetAsync(LinksConsts.LatestVersionJson);
                UpdateManifest manifest = JsonConvert.DeserializeObject<UpdateManifest>(json) ?? throw new Exception("Failed to parse update manifest.");
                bool exeUpdated = false;
                if (manifest.Version != AppConsts.CurrentVersion && manifest.Executable != null && manifest.Executable.UpdateRequired)
                {
                    UpdateBtn.Content = "正在更新…";
                    await UpdateExecutable(manifest.Executable);
                    exeUpdated = true;
                }

                if (manifest.Assets != null && manifest.Assets.Count > 0)
                    await SyncAssets(manifest.Assets);

                if (exeUpdated)
                {
                    UpdateBtn.Content = "更新完成";
                    MessageBox.Show($"主程序已更新至 {manifest.Version}，即将重启。", "更新完成", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    ProcessUtils.StartProcess(PathConsts.CurrentExe, $"{AppConsts.WaitForParentArgument} {Process.GetCurrentProcess().Id}", "", false, false);
                    ExitBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
                else MessageBox.Show("所有文件已同步，当前已是最新版本！", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                WriteLog($"Update exception: {ex.Message}", LogLevel.Error, ex);
                MessageBox.Show($"更新数据时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
            finally
            {
                UpdateBtn.IsEnabled = true;
                if (UpdateBtn.Content.ToString() != "更新完成") UpdateBtn.Content = "更新数据";
            }
        }

        private async Task UpdateExecutable(ExecutableInfo exeInfo)
        {
            FileUtils.EnsureDirectoryExists(PathConsts.UpdateDirectory);
            FileUtils.ClearFolder(PathConsts.UpdateDirectory, false);
            string zipPath = $"update_{Guid.NewGuid():N}.zip";
            try
            {
                using (FileStream fs = new(zipPath, FileMode.Create, FileAccess.Write))
                {
                    int partIndex = 1;
                    foreach (string partUrl in exeInfo.Parts)
                    {
                        UpdateProgressText($"下载切片 ({partIndex}/{exeInfo.Parts.Count})");
                        byte[] data = await NetworkUtils.GetByteArrayAsync(partUrl, 120.0);
                        await fs.WriteAsync(data, 0, data.Length);
                        partIndex++;
                    }
                }

                UpdateProgressText("正在解压…");
                ZipFile.ExtractToDirectory(zipPath, PathConsts.UpdateDirectory);

                if (!File.Exists(PathConsts.NewVersionExe)) throw new FileNotFoundException("Main executable not found after extraction!");

                string downloadedHash = FileUtils.CalculateFileHash(PathConsts.NewVersionExe);
                if (!string.Equals(downloadedHash, exeInfo.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Hash mismatch! Expected: {exeInfo.Hash} Actual: {downloadedHash}");

                FileUtils.TryDelete(PathConsts.OldVersionExe, 5, 500);
                File.Move(PathConsts.CurrentExe, PathConsts.OldVersionExe);
                File.Move(PathConsts.NewVersionExe, PathConsts.CurrentExe);
                FileUtils.TryDelete(PathConsts.UpdateDirectory, 5, 500);
            }
            finally
            {
                FileUtils.TryDelete(zipPath, 5, 500);
            }
        }

        private async Task SyncAssets(List<AssetInfo> assets)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (AssetInfo asset in assets)
            {
                string localPath = Path.Combine(baseDir, asset.Path);
                bool needDownload = true;
                if (File.Exists(localPath))
                {
                    if (string.Equals(FileUtils.CalculateFileHash(localPath), asset.Hash, StringComparison.OrdinalIgnoreCase))
                        needDownload = false;
                }

                if (needDownload)
                {
                    UpdateProgressText("正在同步…");
                    FileUtils.EnsureDirectoryExists(Path.GetDirectoryName(localPath));

                    bool result = await NetworkUtils.TryDownloadFile(asset.Url, localPath, (p) => { }, 30.0);
                    if (!result) throw new Exception($"Failed to download asset {asset.Path}");
                }
            }
        }

        private async Task CheckUpdate()
        {
            try
            {
                string json = await NetworkUtils.GetAsync(LinksConsts.LatestVersionJson);
                UpdateManifest manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest != null)
                {
                    bool needUpdate = false;
                    string tipMessage = "";

                    if (manifest.Version != AppConsts.CurrentVersion)
                    {
                        needUpdate = true;
                        tipMessage = $"发现主程序新版本 {manifest.Version}！";
                    }
                    else if (manifest.Assets != null)
                    {
                        bool assetsChanged = await Task.Run(() =>
                        {
                            foreach (var asset in manifest.Assets)
                            {
                                string localPath = Path.Combine(PathConsts.CurrentDirectory, asset.Path);
                                if (!File.Exists(localPath) || !string.Equals(FileUtils.CalculateFileHash(localPath), asset.Hash, StringComparison.OrdinalIgnoreCase))
                                    return true;
                            }
                            return false;
                        });

                        if (assetsChanged)
                        {
                            needUpdate = true;
                            tipMessage = "发现新的配置或数据文件更新！";
                        }
                    }

                    if (needUpdate)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (MessageBox.Show(tipMessage + "\n是否立即更新？", "发现更新", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None) == MessageBoxResult.Yes)
                                UpdateBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Auto-update check failed: {ex.Message}", LogLevel.Error, ex);
            }
        }

        private void UpdateProgressText(string text) => Dispatcher.Invoke(() => UpdateBtn.Content = text);

        #endregion

        #region Other Events & Animations

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshBtn.IsEnabled = false;
            UpdateServiceStatus();
            await ConfigManager.Instance.LoadAsync();
            ApplySettings();
            WriteLog("Manual refresh triggered.", LogLevel.Info);
            RefreshBtn.IsEnabled = true;
        }

        private async void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("还原数据功能用于将本程序关联的数据文件恢复为初始状态。\r\n当您认为本程序更新造成了关联的数据文件损坏，或您对有关规则做出了修改时可以使用此功能。\r\n是否还原数据？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ResetBtn.IsEnabled = false;
                ResetBtn.Content = "还原中…";
                try
                {
                    await _proxyService.StopAsync(null);
                    string[] files = [PathConsts.NginxConfig, PathConsts.SNIBypassCrt, PathConsts.ProxyRules, PathConsts.AcrylicHosts, PathConsts.NginxCacheDirectory, PathConsts.ProxyRules, PathConsts.ConfigJson];
                    FileUtils.TryDelete(files);
                    _startupService.InitializeDirectoriesAndFiles();
                    await ConfigManager.Instance.LoadAsync();
                    ApplySettings();
                }
                catch (Exception ex)
                {
                    WriteLog("Reset exception.", LogLevel.Error, ex);
                    MessageBox.Show($"还原数据时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ResetBtn.IsEnabled = true;
                    ResetBtn.Content = "还原数据";
                }
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != sender) return;
            if (sender is TabControl tabControl && tabControl.SelectedItem is TabItem selectedItem)
            {
                string header = selectedItem.Header.ToString();
                if (header == "设置")
                {
                    UpdateTempFilesSize();
                    if (!_tempFilesTimer.IsEnabled) _tempFilesTimer.Start();
                }
                else
                {
                    if (_tempFilesTimer.IsEnabled) _tempFilesTimer.Stop();
                    UpdateServiceStatus();
                }
            }
        }

        private async void TraceNginxLogBtn_Click(object sender, RoutedEventArgs e)
        {
            TraceNginxLogBtn.IsEnabled = false;
            await Task.Run(async () =>
            {
                await TailUtils.StopTracking(PathConsts.NginxAccessLog);
                await TailUtils.StopTracking(PathConsts.NginxErrorLog);
                TailUtils.StartTracking(PathConsts.NginxAccessLog, "AccessLog");
                TailUtils.StartTracking(PathConsts.NginxErrorLog, "ErrorLog");
            });
            TraceNginxLogBtn.IsEnabled = true;
        }

        private void InstallCertBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CertificateUtils.IsCertificateInstalled(AppConsts.CertificateThumbprint))
                    CertificateUtils.UninstallCertificate(AppConsts.CertificateThumbprint);

                CertificateUtils.InstallCertificate(PathConsts.CA);
                MessageBox.Show("证书安装成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                WriteLog($"Certificate installation error: {ex.Message}", LogLevel.Error, ex);
                MessageBox.Show($"安装证书时发生异常。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
        }

        private void ToggleButtonsClick(object sender, RoutedEventArgs e) =>
            ApplyBtn.IsEnabled = UnchangeBtn.IsEnabled = true;

        private async void UnchangeBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyBtn.IsEnabled = UnchangeBtn.IsEnabled = false;
            await ConfigManager.Instance.LoadAsync();
            ApplySettings();
        }

        private void AllOnBtn_Click(object sender, RoutedEventArgs e)
        {
            bool changed = false;
            foreach (var item in CollectionConsts.Switches)
            {
                string safeName = $"Toggle_{item.Id.ToSafeIdentifier()}";
                ToggleButton tb = (ToggleButton)Switchlist.FindName(safeName);

                if (tb != null && !tb.IsChecked.GetValueOrDefault())
                {
                    tb.IsChecked = true;
                    changed = true;
                }
            }

            if (changed) ApplyBtn.IsEnabled = UnchangeBtn.IsEnabled = true;
        }

        private void AllOffBtn_Click(object sender, RoutedEventArgs e)
        {
            bool changed = false;
            foreach (var item in CollectionConsts.Switches)
            {
                string safeName = $"Toggle_{item.Id.ToSafeIdentifier()}";
                ToggleButton tb = (ToggleButton)Switchlist.FindName(safeName);

                if (tb != null && tb.IsChecked.GetValueOrDefault())
                {
                    tb.IsChecked = false;
                    changed = true;
                }
            }

            if (changed) ApplyBtn.IsEnabled = UnchangeBtn.IsEnabled = true;
        }

        private void CustomBkgBtn_Click(object sender, RoutedEventArgs e)
        {
            CustomBkgBtn.IsEnabled = false;
            AnimateWindow(1.0, 0.0, () =>
            {
                Hide();
                HideContent();
                new CustomBackgroundWindow().ShowDialog();
                Show();
                AnimateWindow(0.0, 1.0, () => { Activate(); ShowContent(); });
                CustomBkgBtn.IsEnabled = true;
            });
        }

        private void DefaultBkgBtn_Click(object sender, RoutedEventArgs e)
        {
            FileUtils.ClearFolder(PathConsts.BackgroundDirectory, false);
            foreach (var pair in CollectionConsts.DefaultBackgroundMap)
                FileUtils.ExtractResourceToFile(pair.Value, Path.Combine(PathConsts.BackgroundDirectory, pair.Key));

            var imgs = AppConsts.ImageExtensions.SelectMany(ext => Directory.GetFiles(PathConsts.BackgroundDirectory, "*" + ext));

            List<string> imageOrder = [.. imgs.Select(FileUtils.CalculateFileHash)];

            var config = ConfigManager.Instance.Settings.Background;
            config.ImageOrder = imageOrder;
            config.ChangeInterval = 15;
            config.ChangeMode = ConfigConsts.SequentialMode;
            ConfigManager.Instance.Save();

            BackgroundService.CleanAllCache();
            BackgroundService.ReloadConfig();
            BackgroundService.ValidateCurrentImage();
        }

        private void LinkText_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            string url = (sender as TextBlock)?.Text ?? (sender as Run)?.Text;
            if (!string.IsNullOrEmpty(url))
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
                ProcessUtils.StartProcess(url, "", "", true, false);
            }
        }

        private void MenuItem_ShowMainWin_Click(object sender, RoutedEventArgs e)
        {
            if ((Math.Abs(Opacity) < 1E-06 || !IsVisible) && !IsAnyDialogOpen())
            {
                Show();
                AnimateWindow(0.0, 1.0, () => { Activate(); ShowContent(); });
            }
            else Activate();
        }

        private async void MenuItem_StartService_Click(object sender, RoutedEventArgs e)
        {
            bool success = await ExecuteStartServiceAsync(false);
            if (success)
            {
                TaskbarIcon.ShowBalloonTip("成功启动服务", "您现在可以尝试访问列表中的网站\nฅ^•ω•^ฅ", BalloonIcon.Info);
            }
        }

        private async void MenuItem_StopService_Click(object sender, RoutedEventArgs e)
        {
            bool success = await ExecuteStopServiceAsync(false);
            if (success)
            {
                TaskbarIcon.ShowBalloonTip("成功停止服务", "感谢您的使用\n^›⩊‹^ ੭", BalloonIcon.Info);
            }
        }

        private void MenuItem_ExitTool_Click(object sender, RoutedEventArgs e) =>
            ExitBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        public async Task TaskbarIcon_LeftClick()
        {
            MenuItem_ShowMainWin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await UpdateYiyan();
        }

        private void MinimizeToTrayBtn_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTrayBtn.IsEnabled = false;
            AnimateWindow(1.0, 0.0, () =>
            {
                Hide();
                HideContent();
                TaskbarIcon.ShowBalloonTip("已最小化运行", "点击托盘图标显示主界面或右键显示菜单。", BalloonIcon.Info);
                MinimizeToTrayBtn.IsEnabled = true;
            });
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            MinimizeToTrayBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private void MenuItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is MenuItem item)
            {
                string color = item.Header.ToString() switch
                {
                    "停止服务" => "#FFFF0000",
                    "启动服务" => "#FF2BFF00",
                    "退出工具" => "#FFFF00C7",
                    _ => "#00A2FF"
                };
                item.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                item.Header = $"『{item.Header}』";
                item.FontSize += 2.0;
            }
        }

        private void MenuItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is MenuItem item)
            {
                item.Foreground = new SolidColorBrush(Colors.White);
                item.FontSize -= 2.0;
                string header = item.Header.ToString();
                if (header.StartsWith("『") && header.EndsWith("』"))
                    item.Header = header.Substring(1, header.Length - 2);
            }
        }

        private void DebugModeBtn_Click(object sender, RoutedEventArgs e)
        {
            bool current = ConfigManager.Instance.Settings.Advanced.DebugMode;
            if (!current && MessageBox.Show("调试模式仅用于开发和问题诊断，不当启用可能导致非预期行为。建议仅在开发者指导下开启。\n是否继续启用？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.None) == MessageBoxResult.Yes)
                ConfigManager.Instance.Settings.Advanced.DebugMode = true;
            else
            {
                ConfigManager.Instance.Settings.Advanced.DebugMode = false;
                ConfigManager.Instance.Settings.Advanced.GUIDebug = false;
                ConfigManager.Instance.Settings.Advanced.AcrylicDebug = false;
            }
            ConfigManager.Instance.Save();
            ApplySettings();
        }

        private void AcrylicDebugBtn_Click(object sender, RoutedEventArgs e)
        {
            bool current = ConfigManager.Instance.Settings.Advanced.AcrylicDebug;
            if (!current && MessageBox.Show("开启 DNS 服务调试功能，可帮助诊断网络流量走向相关问题。该设置将在服务重启后生效。\n请在重启服务并复现问题后，将相关信息提交给开发者。\n是否确认开启 DNS 服务调试？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None) == MessageBoxResult.Yes)
                ConfigManager.Instance.Settings.Advanced.AcrylicDebug = true;
            else ConfigManager.Instance.Settings.Advanced.AcrylicDebug = false;
            ConfigManager.Instance.Save();
            ApplySettings();
        }

        private async void GUIDebugBtn_Click(object sender, RoutedEventArgs e)
        {
            bool current = ConfigManager.Instance.Settings.Advanced.GUIDebug;

            if (!current && MessageBox.Show("开启 GUI 调试模式有助于更精准地定位问题，但生成日志会增加一定的性能开销，建议在不使用时及时关闭。\n开启后程序将自动退出，重启后生效。\n请您在重启并复现问题后，将相关信息提交给开发者。\n是否确认开启 GUI 调试模式并重启程序？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None) == MessageBoxResult.Yes)
            {
                GUIDebugBtn.IsEnabled = false;

                ConfigManager.Instance.Settings.Advanced.GUIDebug = true;
                await ConfigManager.Instance.SaveNowAsync();

                ProcessUtils.StartProcess(PathConsts.CurrentExe, $"{AppConsts.WaitForParentArgument} {Process.GetCurrentProcess().Id}", "", false, false);
                Exit(false);
            }
            else if (current)
            {
                ConfigManager.Instance.Settings.Advanced.GUIDebug = false;
                ConfigManager.Instance.Save();
                ApplySettings();
            }
        }

        private void EditHostsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FileUtils.EnsureFileExists(PathConsts.SystemHosts);
                ProcessUtils.StartProcess("notepad.exe", PathConsts.SystemHosts, "", true, false);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception opening Hosts file: {ex.Message}", LogLevel.Error, ex);
                MessageBox.Show($"打开 {PathConsts.SystemHosts} 时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FeedbackBtn_Click(object sender, RoutedEventArgs e)
        {
            string message = "如果您在使用过程中遇到问题或有任何建议，欢迎通过以下方式联系我们：\n" +
                             "● QQ 交流群：946813204\n" +
                             "● 电子邮件：hi@racpast.com 或 racpast@gmail.com\n" +
                             "是否立即跳转加入 QQ 群？";

            if (MessageBox.Show(message, "反馈与建议", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                ProcessUtils.StartProcess(LinksConsts.QqGroupJoinUrl, useShellExecute:true);
        }

        private void HelpBtn_HowToFindActiveAdapter_Click(object sender, RoutedEventArgs e) =>
            ProcessUtils.StartProcess(LinksConsts.AdapterUncertaintyHelpLink, useShellExecute: true);

        private void AutoCheckUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            bool current = ConfigManager.Instance.Settings.Program.AutoCheckUpdate;
            ConfigManager.Instance.Settings.Program.AutoCheckUpdate = !current;
            ConfigManager.Instance.Save();
            ApplySettings();
        }

        private void ThemeSwitchTB_Checked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(true);
            ConfigManager.Instance.Settings.Program.ThemeMode = ConfigConsts.DarkMode;
            ConfigManager.Instance.Save();
        }

        private void ThemeSwitchTB_Unchecked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(false);
            ConfigManager.Instance.Settings.Program.ThemeMode = ConfigConsts.LightMode;
            ConfigManager.Instance.Save();
        }

        public void SwitchTheme(bool isNightMode)
        {
            Color targetBackground = isNightMode ? Color.FromArgb(112, 97, 97, 97) : Color.FromArgb(112, 255, 255, 255);
            Color targetBorder = isNightMode ? Colors.Black : Colors.White;
            Color targetSwitchText = isNightMode ? (Color)ColorConverter.ConvertFromString("#C4C9D4") : (Color)ColorConverter.ConvertFromString("#F3C62B");

            Application.Current.Resources["BackgroundColor"] = targetBackground;
            Application.Current.Resources["BorderColor"] = targetBorder;

            TimeSpan duration = TimeSpan.FromSeconds(0.8);
            QuadraticEase easing = new() { EasingMode = EasingMode.EaseInOut };

            if (Application.Current.Resources["BackgroundBrush"] is SolidColorBrush bgBrush)
                bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetBackground, duration) { EasingFunction = easing });

            if (Application.Current.Resources["BorderBrush"] is SolidColorBrush borderBrush)
                borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetBorder, duration) { EasingFunction = easing });

            if (ThemeSwitchTBText.Foreground is SolidColorBrush textBrush)
                textBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetSwitchText, duration) { EasingFunction = easing });
        }

        private void OnBackgroundChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "CurrentImage") return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                NextImage.Opacity = 0.0;
                NextImage.Source = BackgroundService.CurrentImage;

                DoubleAnimation fadeOut = new(1.0, 0.0, TimeSpan.FromSeconds(1.0));
                DoubleAnimation fadeIn = new(0.0, 1.0, TimeSpan.FromSeconds(1.0));

                CurrentImage.BeginAnimation(OpacityProperty, fadeOut);
                NextImage.BeginAnimation(OpacityProperty, fadeIn);

                (NextImage, CurrentImage) = (CurrentImage, NextImage);
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            BackgroundService.Cleanup();
            BackgroundService.PropertyChanged -= OnBackgroundChanged;
            base.OnClosed(e);
        }

        private static bool IsAnyDialogOpen()
        {
            foreach (Window window in Application.Current.Windows)
                if (window is CustomBackgroundWindow) return true;
            return false;
        }

        private void AnimateWindow(double from, double to, Action onCompleted = null)
        {
            DoubleAnimation animation = new()
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            if (onCompleted != null) animation.Completed += (s, e) => onCompleted();
            BeginAnimation(OpacityProperty, animation);
        }

        private void HideContent()
        {
            TransitioningContentControlA.Hide();
            TransitioningContentControlB.Hide();
            TransitioningContentControlC.Hide();
        }

        private void ShowContent()
        {
            TransitioningContentControlA.Show();
            TransitioningContentControlB.Show();
            TransitioningContentControlC.Show();
        }

        private void EnableAutoStartBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _startupService.CreateTask(AppConsts.TaskName, "开机启动 SNIBypassGUI 并启动服务。", "SNIBypassGUI", PathConsts.CurrentExe, AppConsts.AutoStartArgument);
                MessageBox.Show("已成功设置 SNIBypassGUI 开机启动。\n开机后，程序将自动运行至系统托盘并启动服务。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置开机启动时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
        }

        private void DisableAutoStartBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _startupService.CreateTask(AppConsts.TaskName, "开机启动 SNIBypassGUI 并自动清理。", "SNIBypassGUI", PathConsts.CurrentExe, AppConsts.CleanUpArgument);
                MessageBox.Show("已成功取消 SNIBypassGUI 的开机启动。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"取消开机启动时发生错误。\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            ExitBtn.IsEnabled = false;
            Exit(true);
        }

        private void Exit(bool stopTail = true)
        {
            AnimateWindow(1, 0, async () =>
            {
                Hide();
                TaskbarIcon.Visibility = Visibility.Collapsed;
                TrayIconUtils.RefreshNotification();

                await _proxyService.RemoveHostsRecordsAsync();
                await _proxyService.StopAsync(null);

                if (stopTail) await TailUtils.StopTracking();
                NetworkUtils.FlushDNS();

                Environment.Exit(0);
            });
        }

        #endregion
    }
}
