using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public static class TaskbarController
{
    public static bool IsHidden { get; private set; }

    public static void Show() => SetVisibility(true);

    public static void Hide() => SetVisibility(false);

    public static void Toggle()
    {
        if (IsHidden) Show();
        else Hide();
    }

    private static void SetVisibility(bool visible)
    {
        foreach (var hwnd in FindAllTaskbars())
        {
            User32.ShowWindow(hwnd, visible ? User32.SW_SHOW : User32.SW_HIDE);
        }
        IsHidden = !visible;
    }

    /// Основная панель задач (Shell_TrayWnd) + панели на дополнительных мониторах
    /// (Shell_SecondaryTrayWnd), если включён показ панели задач на всех экранах
    private static List<IntPtr> FindAllTaskbars()
    {
        var result = new List<IntPtr>();

        var primary = User32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) result.Add(primary);

        User32.EnumWindows((hwnd, _) =>
        {
            var sb = new System.Text.StringBuilder(256);
            User32.GetClassName(hwnd, sb, sb.Capacity);
            if (sb.ToString() == "Shell_SecondaryTrayWnd")
            {
                result.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }
}