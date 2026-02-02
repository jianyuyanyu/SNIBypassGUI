using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Common.Network;
using SNIBypassGUI.Common.System;
using SNIBypassGUI.Common.Tools;
using SNIBypassGUI.Consts;
using SNIBypassGUI.Models;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Services
{
    public class ProxyService
    {
        private readonly SemaphoreSlim _serviceLock = new(1, 1);

        /// <summary>
        /// Starts Nginx and Acrylic services, and configures the adapter DNS.
        /// </summary>
        public async Task StartAsync(Action<string> statusCallback)
        {
            if (!await _serviceLock.WaitAsync(0)) return;

            try
            {
                // 1. Start Nginx
                if (!ProcessUtils.IsProcessRunning(AppConsts.NginxProcessName))
                {
                    statusCallback?.Invoke("主服务启动中");
                    await StartNginx();
                }

                // 2. Start DNS (Acrylic)
                if (!AcrylicUtils.IsAcrylicServiceRunning())
                {
                    statusCallback?.Invoke("DNS服务启动中");
                    await StartAcrylic();
                }

                // 3. Set Adapter DNS
                var adapters = await NetworkAdapterUtils.GetNetworkAdaptersAsync(NetworkAdapterUtils.ScopeNeeded.FriendlyNameNotNullOnly);
                string selectedAdapterName = ConfigManager.Instance.Settings.Program.SpecifiedAdapter;

                var activeAdapter = adapters.FirstOrDefault(a => a.FriendlyName == selectedAdapterName);

                if (activeAdapter != null)
                {
                    WriteLog($"Setting DNS for adapter: {activeAdapter.FriendlyName}", LogLevel.Info);
                    await SetLoopbackDNSAsync(activeAdapter);
                    await Task.Run(() => NetworkUtils.FlushDNS());
                }
                else WriteLog("Specified network adapter not found during start sequence.", LogLevel.Warning);
            }
            finally
            {
                _serviceLock.Release();
            }
        }

        /// <summary>
        /// Stops services and restores adapter DNS.
        /// </summary>
        public async Task StopAsync(Action<string> statusCallback)
        {
            if (!await _serviceLock.WaitAsync(0)) return;

            try
            {
                // 1. Stop Nginx
                if (ProcessUtils.IsProcessRunning(AppConsts.NginxProcessName))
                {
                    statusCallback?.Invoke("主服务停止中");
                    try
                    {
                        await Task.Run(() => ProcessUtils.KillProcess(AppConsts.NginxProcessName));
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error stopping Core Service: {ex.Message}", LogLevel.Error, ex);
                    }
                }

                // 2. Stop DNS
                if (AcrylicUtils.IsAcrylicServiceRunning())
                {
                    statusCallback?.Invoke("DNS服务停止中");
                    try
                    {
                        if (AcrylicUtils.IsAcrylicServiceHitLogEnabled())
                            await TailUtils.StopTracking(AcrylicUtils.GetLogPath());

                        await Task.Run(() => AcrylicUtils.StopAcrylicService());
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error stopping DNS Service: {ex.Message}", LogLevel.Error, ex);
                    }
                }

                // 3. Restore Adapter
                string specifiedAdapter = ConfigManager.Instance.Settings.Program.SpecifiedAdapter;
                var adapters = await NetworkAdapterUtils.GetNetworkAdaptersAsync(NetworkAdapterUtils.ScopeNeeded.FriendlyNameNotNullOnly);
                var activeAdapter = adapters.FirstOrDefault(a => a.FriendlyName == specifiedAdapter);

                if (activeAdapter != null) await RestoreAdapterDNSAsync(activeAdapter);
            }
            finally
            {
                _serviceLock.Release();
            }
        }

        private async Task StartNginx()
        {
            try
            {
                if (NetworkUtils.IsPortInUse(80, false) || NetworkUtils.IsPortInUse(443, false))
                {
                    WriteLog("Port 80 or 443 is in use. Nginx might fail to start.", LogLevel.Warning);
                }
                await Task.Run(() => ProcessUtils.StartProcess(PathConsts.Nginx, "", PathConsts.NginxDirectory, false, true));
            }
            catch (Exception ex)
            {
                WriteLog("Exception starting Core Service.", LogLevel.Error, ex);
                throw;
            }
        }

        private async Task StartAcrylic()
        {
            try
            {
                if (AcrylicUtils.IsAcrylicServiceHitLogEnabled())
                {
                    AcrylicUtils.EnableAcrylicServiceHitLog();
                    TailUtils.StartTracking(AcrylicUtils.GetLogPath(), "HitLog", false);
                }
                await AcrylicUtils.StartAcrylicService();
            }
            catch (Exception ex)
            {
                WriteLog("Exception starting DNS Service.", LogLevel.Error, ex);
                throw;
            }
        }

        public async Task SetLoopbackDNSAsync(NetworkAdapter Adapter)
        {
            try
            {
                await Task.Run(async () =>
                {
                    bool isAlreadyLoopback = Adapter.IPv4DNSServer.Length > 0 && Adapter.IPv4DNSServer[0] == "127.0.0.1";
                    var tempStore = ConfigManager.Instance.Settings.TemporaryData;

                    if (!isAlreadyLoopback)
                    {
                        WriteLog($"Backing up DNS settings for adapter {Adapter.FriendlyName}...", LogLevel.Info);

                        // Create backup object
                        var backupInfo = new AdapterBackupInfo
                        {
                            IsIPv4Auto = Adapter.IsIPv4DNSAuto,
                            IsIPv6Auto = Adapter.IsIPv6DNSAuto
                        };

                        foreach (var dns in Adapter.IPv4DNSServer)
                            if (NetworkUtils.IsValidIPv4(dns) && dns != "127.0.0.1") backupInfo.IPv4Servers.Add(dns);

                        foreach (var dns in Adapter.IPv6DNSServer)
                            if (NetworkUtils.IsValidIPv6(dns) && dns != "::1") backupInfo.IPv6Servers.Add(dns);

                        // Save to dictionary using Adapter Name as Key
                        tempStore[Adapter.FriendlyName] = backupInfo;
                        
                        ConfigManager.Instance.Save();
                    }
                    else
                    {
                        // Check if backup exists
                        if (!tempStore.ContainsKey(Adapter.FriendlyName))
                            WriteLog($"Adapter {Adapter.FriendlyName} is already 127.0.0.1 and no backup found. Cannot save original settings.", LogLevel.Warning);
                    }

                    NetworkAdapterUtils.SetIPv4DNS(Adapter, ["127.0.0.1"]);
                    await NetworkAdapterUtils.SetIPv6DNS(Adapter, ["::1"]);

                    var refreshed = await NetworkAdapterUtils.RefreshAsync(Adapter);
                    if (refreshed != null) Adapter = refreshed;
                });
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to set adapter {Adapter.FriendlyName}.", LogLevel.Error, ex);
            }
        }

        public async Task RestoreAdapterDNSAsync(NetworkAdapter Adapter, bool deleteBackup = true)
        {
            try
            {
                await Task.Run(async () =>
                {
                    var tempStore = ConfigManager.Instance.Settings.TemporaryData;

                    // Retrieve backup by Adapter Name
                    if (!tempStore.TryGetValue(Adapter.FriendlyName, out AdapterBackupInfo backup)) 
                        return;

                    WriteLog($"Restoring DNS settings for adapter {Adapter.FriendlyName}...", LogLevel.Info);

                    // Restore IPv4
                    if (backup.IsIPv4Auto)
                        NetworkAdapterUtils.SetIPv4DNS(Adapter, []);
                    else if (backup.IPv4Servers.Count > 0)
                        NetworkAdapterUtils.SetIPv4DNS(Adapter, [.. backup.IPv4Servers]);
                    else
                        NetworkAdapterUtils.SetIPv4DNS(Adapter, []);

                    // Restore IPv6
                    if (backup.IsIPv6Auto)
                        await NetworkAdapterUtils.SetIPv6DNS(Adapter, []);
                    else if (backup.IPv6Servers.Count > 0)
                        await NetworkAdapterUtils.SetIPv6DNS(Adapter, [.. backup.IPv6Servers]);
                    else
                        await NetworkAdapterUtils.SetIPv6DNS(Adapter, []);

                    WriteLog($"Restore command sent for adapter {Adapter.FriendlyName}.", LogLevel.Info);

                    if (deleteBackup)
                    {
                        tempStore.Remove(Adapter.FriendlyName);
                        ConfigManager.Instance.Save();
                    }
                });
            }
            catch (Exception ex)
            {
                WriteLog($"Exception restoring adapter {Adapter.FriendlyName}: {ex.Message}", LogLevel.Error, ex);
            }
        }

        public async Task UpdateHostsFromConfigAsync()
        {
            try
            {
                StringBuilder hostsBuilder = new();
                hostsBuilder.AppendLine("# Generated by SNIBypassGUI");
                hostsBuilder.AppendLine($"# Update Time: {DateTime.Now}");
                hostsBuilder.AppendLine();

                var proxySettings = ConfigManager.Instance.Settings.ProxySettings;

                foreach (var item in CollectionConsts.Switches)
                {
                    if (proxySettings.TryGetValue(item.Id, out bool isEnabled) && isEnabled)
                    {
                        if (!string.IsNullOrEmpty(item.Hosts))
                        {
                            try
                            {
                                byte[] bytes = Convert.FromBase64String(item.Hosts);
                                string decodedRules = Encoding.UTF8.GetString(bytes);
                                string safeRules = decodedRules.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

                                hostsBuilder.AppendLine($"# {item.Id} Start");
                                hostsBuilder.AppendLine(safeRules);
                                hostsBuilder.AppendLine($"# {item.Id} End");
                                hostsBuilder.AppendLine();
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"Error decoding hosts for {item.Id}", LogLevel.Error, ex);
                            }
                        }
                    }
                }

                await FileUtils.WriteAllTextAsync(PathConsts.AcrylicHosts, hostsBuilder.ToString());
            }
            catch (Exception ex)
            {
                WriteLog($"Exception updating Hosts file: {ex.Message}", LogLevel.Error, ex);
            }
        }

        public async Task RemoveHostsRecordsAsync()
        {
            try
            {
                await FileUtils.WriteAllTextAsync(PathConsts.AcrylicHosts, "# SNIBypassGUI Hosts Cleared");
            }
            catch (Exception ex)
            {
                WriteLog($"Exception removing Hosts records: {ex.Message}", LogLevel.Error, ex);
            }
        }
    }
}
