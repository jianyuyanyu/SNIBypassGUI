using System;

namespace SNIBypassGUI.Models
{
    /// <summary>
    /// Represents a network adapter with its configuration and status.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NetworkAdapter"/> class.
    /// </remarks>
    public class NetworkAdapter(
        string name,
        string friendlyName,
        string description,
        uint interfaceIndex,
        Guid guid,
        bool isNetEnabled,
        ushort netConnectionStatus,
        string[] ipv4DnsServer,
        bool isIPv4DNSAuto,
        string[] ipv6DnsServer,
        bool isIPv6DNSAuto)
    {
        /// <summary>
        /// Gets the internal name of the adapter (Win32_NetworkAdapter.Name).
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Gets the name shown in the Control Panel (Win32_NetworkAdapter.NetConnectionID).
        /// </summary>
        public string FriendlyName { get; } = friendlyName;

        /// <summary>
        /// Gets the hardware description of the adapter.
        /// </summary>
        public string Description { get; } = description;

        /// <summary>
        /// Gets the index of the network interface.
        /// </summary>
        public uint InterfaceIndex { get; } = interfaceIndex;

        /// <summary>
        /// Gets the globally unique identifier (GUID) of the adapter.
        /// </summary>
        public Guid GUID { get; } = guid;

        /// <summary>
        /// Gets a value indicating whether the adapter is enabled.
        /// </summary>
        public bool IsNetEnabled { get; } = isNetEnabled;

        /// <summary>
        /// Gets the connection status of the adapter (e.g., 2 = Connected).
        /// </summary>
        public ushort NetConnectionStatus { get; } = netConnectionStatus;

        /// <summary>
        /// Gets the list of configured IPv4 DNS servers.
        /// </summary>
        public string[] IPv4DNSServer { get; } = isIPv4DNSAuto ? [] : (ipv4DnsServer ?? []);

        /// <summary>
        /// Gets a value indicating whether IPv4 DNS is configured to be obtained automatically.
        /// </summary>
        public bool IsIPv4DNSAuto { get; } = isIPv4DNSAuto;

        /// <summary>
        /// Gets the list of configured IPv6 DNS servers.
        /// </summary>
        public string[] IPv6DNSServer { get; } = isIPv6DNSAuto ? [] : (ipv6DnsServer ?? []);

        /// <summary>
        /// Gets a value indicating whether IPv6 DNS is configured to be obtained automatically.
        /// </summary>
        public bool IsIPv6DNSAuto { get; } = isIPv6DNSAuto;

        /// <summary>
        /// Gets the Registry-compatible ID string (GUID wrapped in braces).
        /// </summary>
        public string Id => GUID.ToString("B");
    }
}
