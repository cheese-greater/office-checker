using System;
using System.Runtime.InteropServices;

namespace OfficeLockChecker
{
    // Win32 messages and calls used to draw the XP window frame and list header ourselves.
    internal static class NativeMethods
    {
        public const int WM_PAINT = 0x000F;
        public const int WM_SETTEXT = 0x000C;
        public const int WM_STYLECHANGING = 0x007C;
        public const int WM_SETICON = 0x0080;
        public const int WM_NCCALCSIZE = 0x0083;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_NCPAINT = 0x0085;
        public const int WM_NCACTIVATE = 0x0086;
        // Undocumented messages the themed DefWindowProc uses to paint the caption and frame.
        public const int WM_NCUAHDRAWCAPTION = 0x00AE;
        public const int WM_NCUAHDRAWFRAME = 0x00AF;

        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        public const int GWL_STYLE = -16;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_THICKFRAME = 0x00040000;
        public const int WS_EX_DLGMODALFRAME = 0x00000001;
        public const int WS_EX_WINDOWEDGE = 0x00000100;
        public const int WS_EX_CLIENTEDGE = 0x00000200;

        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_FRAMECHANGED = 0x0020;

        public const int LVM_GETHEADER = 0x101F;
        public const int HDM_GETITEMCOUNT = 0x1200;
        public const int HDM_GETITEMRECT = 0x1207;

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // STYLESTRUCT, passed with WM_STYLECHANGING.
        [StructLayout(LayoutKind.Sequential)]
        public struct StyleChange
        {
            public int OldStyle;
            public int NewStyle;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, int flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr windowHandle, int index, int newValue);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wordParameter, IntPtr longParameter);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wordParameter, ref NativeRectangle longParameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr windowHandle, out NativeRectangle clientBounds);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);
    }
}
