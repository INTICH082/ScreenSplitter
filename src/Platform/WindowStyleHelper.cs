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