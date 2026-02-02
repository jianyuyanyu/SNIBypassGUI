using System;
using SNIBypassGUI.Common.Interop;

namespace SNIBypassGUI.Common.UI
{
    public static class TrayIconUtils
    {
        /// <summary>
        /// Refreshes the system notification area (System Tray) to remove stale icons.
        /// </summary>
        public static void RefreshNotification()
        {
            var notifyAreaHandle = GetNotifyAreaHandle();
            if (notifyAreaHandle != IntPtr.Zero) RefreshWindow(notifyAreaHandle);

            var notifyOverHandle = GetNotifyOverHandle();
            if (notifyOverHandle != IntPtr.Zero) RefreshWindow(notifyOverHandle);
        }

        private static void RefreshWindow(IntPtr windowHandle)
        {
            const uint WM_MOUSEMOVE = 0x0200;
            if (User32.GetClientRect(windowHandle, out User32.RECT rect))
            {
                // Simulate mouse move over the tray area
                for (var x = 0; x < rect.Right; x += 5)
                    for (var y = 0; y < rect.Bottom; y += 5)
                        User32.SendMessage(windowHandle, WM_MOUSEMOVE, 0, (y << 16) + x);
            }
        }

        private static IntPtr GetNotifyAreaHandle()
        {
            var trayWndHandle = User32.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            var trayNotifyWndHandle = User32.FindWindowEx(trayWndHandle, IntPtr.Zero, "TrayNotifyWnd", null);
            var sysPagerHandle = User32.FindWindowEx(trayNotifyWndHandle, IntPtr.Zero, "SysPager", null);
            return User32.FindWindowEx(sysPagerHandle, IntPtr.Zero, "ToolbarWindow32", null);
        }

        private static IntPtr GetNotifyOverHandle()
        {
            var overHandle = User32.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "NotifyIconOverflowWindow", null);
            return User32.FindWindowEx(overHandle, IntPtr.Zero, "ToolbarWindow32", null);
        }
    }
}
