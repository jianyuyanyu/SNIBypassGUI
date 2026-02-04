using System.Runtime.InteropServices;

namespace SNIBypassGUI.Common.Interop
{
    public static class Dnsapi
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        public static extern uint DnsFlushResolverCache();
    }
}
