using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public static class TaskbarController
{
    public static bool IsHidden { get; private set; }

    public static void Show()
    {
        var hwnd = User32.FindWindow("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
        {
            User32.ShowWindow(hwnd, User32.SW_SHOW);
        }
        IsHidden = false;
    }

    public static void Hide()
    {
        var hwnd = User32.FindWindow("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
        {
            User32.ShowWindow(hwnd, User32.SW_HIDE);
        }
        IsHidden = true;
    }

    public static void Toggle()
    {
        if (IsHidden) Show();
        else Hide();
    }
}