using System.Runtime.InteropServices;

namespace SNIBypassGUI.Common.Interop
{
    public static class NetApi
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        public static extern uint DnsFlushResolverCache();

        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetBestInterface(uint DestAddr, out uint BestIfIndex);
    }
}
