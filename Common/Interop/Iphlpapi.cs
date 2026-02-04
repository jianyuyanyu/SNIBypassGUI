using System;
using System.Runtime.InteropServices;

namespace SNIBypassGUI.Common.Interop
{
    public static class Iphlpapi
    {
        public const int AF_INET = 2;
        public const int AF_INET6 = 23;
        public const int TCP_TABLE_OWNER_PID_ALL = 5;


        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetBestInterface(uint DestAddr, out uint BestIfIndex);


        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int tableClass,
            int reserved
        );

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint dwState;
            public uint dwLocalAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] dwLocalPort;
            public uint dwRemoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] dwRemotePort;
            public int dwOwningPid;

            public readonly ushort LocalPort => BitConverter.ToUInt16([dwLocalPort[1], dwLocalPort[0]], 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] ucLocalAddr;
            public uint dwLocalScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] dwLocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] ucRemoteAddr;
            public uint dwRemoteScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] dwRemotePort;
            public uint dwState;
            public int dwOwningPid;

            public readonly ushort LocalPort => BitConverter.ToUInt16([dwLocalPort[1], dwLocalPort[0]], 0);
        }
    }
}
