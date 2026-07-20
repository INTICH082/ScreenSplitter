using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public sealed class DisplayChangeWatcher : IDisposable
{
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private static readonly IntPtr SubclassId = new(0x5344_4953); // "SDIS" — просто уникальный id, не 0 (0 занят хоткеями)

    private readonly IntPtr _hwnd;
    private readonly User32.SubclassProc _proc;

    public event Action? DisplayChanged;

    public DisplayChangeWatcher(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = WndProc;
        User32.SetWindowSubclass(_hwnd, _proc, SubclassId, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            try { DisplayChanged?.Invoke(); } catch { /* не роняем приложение из-за подписчика */ }
        }
        return User32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        User32.RemoveWindowSubclass(_hwnd, _proc, SubclassId);
    }
}