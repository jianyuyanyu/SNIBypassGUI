using SNIBypassGUI.Common.Interop;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SNIBypassGUI.Common.IO;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.Network
{
    public static class NetworkUtils
    {
        // Singleton HttpClient instance to prevent socket exhaustion.
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        #region IP & Port Validations
        private static readonly Regex IPv6Regex = new(
            @"^(" +
            @"(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,7}:|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}|" +
            @"[0-9A-Fa-f]{1,4}:(?:(?::[0-9A-Fa-f]{1,4}){1,6})|" +
            @":(?:(?::[0-9A-Fa-f]{1,4}){1,7}|:)|" +
            @"fe80:(?::[0-9A-Fa-f]{0,4}){0,4}%[0-9A-Za-z]+|" +
            @"::(?:ffff(:0{1,4}){0,1}:){0,1}" +
            @"(?:(?:25[0-5]|(?:2[0-4]|1?\d|)\d)\.){3,3}" +
            @"(?:25[0-5]|(?:2[0-4]|1?\d|)\d)|" +
            @"(?:[0-9A-Fa-f]{1,4}:){1,4}:" +
            @"(?:(?:25[0-5]|(?:2[0-4]|1?\d|)\d)\.){3,3}" +
            @"(?:25[0-5]|(?:2[0-4]|1?\d|)\d)" +
            @")$", RegexOptions.Compiled);

        /// <summary>
        /// Determines whether the specified string is a valid IPv4 address (Strict Mode).
        /// Rejects leading zeros (e.g., "192.168.01.1") and non-standard formats.
        /// </summary>
        /// <param name="ipAddress">The IP address string to validate.</param>
        /// <returns>True if the string is a valid IPv4 address; otherwise, false.</returns>
        public static bool IsValidIPv4(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;

            // Split by dot, must strictly have 4 segments
            string[] parts = ipAddress.Split('.');
            if (parts.Length != 4) return false;

            foreach (string part in parts)
            {
                // Segment cannot be empty
                if (string.IsNullOrEmpty(part)) return false;

                // Check for non-digit characters
                foreach (char c in part)
                {
                    if (!char.IsDigit(c)) return false;
                }

                // Check for leading zeros (e.g., "01" is invalid, but "0" is valid)
                if (part.Length > 1 && part[0] == '0') return false;

                // Parse to integer and check range (0-255)
                if (!int.TryParse(part, out int num) || num < 0 || num > 255)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether the specified string is a valid IPv6 address using Regex.
        /// </summary>
        /// <param name="ipAddress">The IP address string to validate.</param>
        /// <returns>True if the string is a valid IPv6 address; otherwise, false.</returns>
        public static bool IsValidIPv6(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;
            return IPv6Regex.IsMatch(ipAddress);
        }

        /// <summary>
        /// Determines whether the specified string is a valid IP address (IPv4 or IPv6).
        /// </summary>
        /// <param name="ipAddress">The IP address string to validate.</param>
        /// <returns>True if the string is a valid IP address; otherwise, false.</returns>
        public static bool IsValidIP(string ipAddress) =>
            IsValidIPv4(ipAddress) || IsValidIPv6(ipAddress);

        /// <summary>
        /// Checks if a specific port is in use.
        /// </summary>
        /// <param name="port">The port number to check.</param>
        /// <param name="checkUdp">If true, checks UDP listeners; otherwise, checks TCP listeners (default).</param>
        /// <returns>True if the port is occupied; otherwise, false.</returns>
        public static bool IsPortInUse(int port, bool checkUdp = false)
        {
            try
            {
                IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();

                // Check TCP Listeners (Common for Web Servers like Nginx)
                IPEndPoint[] tcpEndPoints = ipProperties.GetActiveTcpListeners();
                if (tcpEndPoints.Any(endPoint => endPoint.Port == port)) return true;

                // Check UDP Listeners (For QUIC/HTTP3 or DNS)
                if (checkUdp)
                {
                    IPEndPoint[] udpEndPoints = ipProperties.GetActiveUdpListeners();
                    if (udpEndPoints.Any(endPoint => endPoint.Port == port)) return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while checking port {port}.", LogLevel.Error, ex);
                return false;
            }
        }

        #endregion

        #region DNS Operations

        /// <summary>
        /// Flushes the Windows DNS Resolver Cache.
        /// </summary>
        /// <returns>True if successful; otherwise, false.</returns>
        public static bool FlushDNS()
        {
            try
            {
                // Call the low-level API defined in WinApiUtils
                // DnsFlushResolverCache returns a BOOL (non-zero is success, zero is failure)
                uint result = NetApi.DnsFlushResolverCache();

                if (result != 0)
                {
                    WriteLog("DNS Resolver Cache flushed successfully.", LogLevel.Info);
                    return true;
                }
                else
                {
                    // Note: DnsFlushResolverCache rarely fails unless service is down, but good to handle it.
                    int err = Marshal.GetLastWin32Error();
                    WriteLog($"Failed to flush DNS Resolver Cache. Win32 Error: {err}", LogLevel.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteLog("Exception occurred while flushing DNS cache.", LogLevel.Error, ex);
                return false;
            }
        }

        #endregion

        #region HTTP Operations

        /// <summary>
        /// Performs an asynchronous GET request and returns the response as a string.
        /// </summary>
        public static async Task<string> GetAsync(string url, double timeOut = 10, string userAgent = "Mozilla/5.0")
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeOut));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);

            using var response = await _httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Performs an asynchronous GET request and returns the response as a byte array.
        /// </summary>
        public static async Task<byte[]> GetByteArrayAsync(string url, double timeOut = 60, string userAgent = "Mozilla/5.0")
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeOut));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);

            using var response = await _httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Tries to download a file from a URL to a local path with progress reporting.
        /// Returns false if failed, instead of throwing exceptions.
        /// </summary>
        public static async Task<bool> TryDownloadFile(string url, string savePath, Action<double> updateProgress = null, double timeOut = 60, string userAgent = "Mozilla/5.0")
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeOut));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(userAgent);

                // Use ResponseHeadersRead to avoid buffering the entire file into memory first
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    WriteLog($"Download failed. Url: {url}, Status: {response.StatusCode}", LogLevel.Warning);
                    return false;
                }

                // Ensure the destination directory exists
                string dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir)) FileUtils.EnsureDirectoryExists(dir);

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                long bytesDownloaded = 0;
                byte[] buffer = new byte[8192]; // 8KB buffer
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    bytesDownloaded += bytesRead;

                    if (totalBytes > 0) updateProgress?.Invoke((double)bytesDownloaded / totalBytes * 100);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                WriteLog($"Download timed out or canceled: {url}", LogLevel.Warning);
                return false;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while downloading file from {url}.", LogLevel.Error, ex);
                return false;
            }
        }

        #endregion
    }
}
