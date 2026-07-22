using System.Runtime.InteropServices;
using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public static class WindowStyleHelper
{
    public static void MakeNonActivating(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(
            hwnd,
            User32.GWL_EXSTYLE,
            exStyle | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW);
    }

    public static void MoveWindow(IntPtr hwnd, int x, int y, int width, int height)
    {
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        User32.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static void PlaceWindowFlush(IntPtr hwnd, int targetX, int targetY, int targetWidth, int targetHeight)
    {
        if (hwnd == IntPtr.Zero) return;

        var (x, y, w, h) = ComputeFlushRect(hwnd, targetX, targetY, targetWidth, targetHeight);
        MoveWindow(hwnd, x, y, w, h);
    }

    public static void PlaceWindowFlushTopmost(IntPtr hwnd, int targetX, int targetY, int targetWidth, int targetHeight)
    {
        if (hwnd == IntPtr.Zero) return;

        var (x, y, w, h) = ComputeFlushRect(hwnd, targetX, targetY, targetWidth, targetHeight);

        const uint SWP_NOACTIVATE = 0x0010;
        var hwndTopmost = new IntPtr(-1);
        User32.SetWindowPos(hwnd, hwndTopmost, x, y, w, h, SWP_NOACTIVATE);
    }

    private static (int X, int Y, int Width, int Height) ComputeFlushRect(
        IntPtr hwnd, int targetX, int targetY, int targetWidth, int targetHeight)
    {
        int left = 0, top = 0, right = 0, bottom = 0;

        if (User32.GetWindowRect(hwnd, out var actual) &&
            Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, Marshal.SizeOf<User32.RECT>()) == 0)
        {
            left = visible.Left - actual.Left;
            top = visible.Top - actual.Top;
            right = actual.Right - visible.Right;
            bottom = actual.Bottom - visible.Bottom;
        }

        return (targetX - left, targetY - top, targetWidth + left + right, targetHeight + top + bottom);
    }

    public static void MakeClickThrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(
            hwnd,
            User32.GWL_EXSTYLE,
            exStyle | User32.WS_EX_TRANSPARENT | User32.WS_EX_LAYERED
                    | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW);
    }

    public static void ActivateWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        User32.ShowWindow(hwnd, User32.SW_RESTORE);
        User32.SetForegroundWindow(hwnd);
    }
}