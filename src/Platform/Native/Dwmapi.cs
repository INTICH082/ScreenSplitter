using System.Runtime.InteropServices;

namespace ScreenSplitter.Platform.Windows.Native;

public static class Dwmapi
{
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int dwAttribute, out User32.RECT pvAttribute, int cbAttribute);
}