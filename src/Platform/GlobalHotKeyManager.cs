using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public sealed class GlobalHotKeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly User32.SubclassProc _proc;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;
    private bool _subclassed;
    private bool _disposed;

    public GlobalHotKeyManager(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            throw new ArgumentException("Окно ещё не создано (hwnd == IntPtr.Zero).", nameof(hwnd));

        _hwnd = hwnd;
        _proc = WndProc;
        _subclassed = User32.SetWindowSubclass(_hwnd, _proc, IntPtr.Zero, IntPtr.Zero);
    }

    public int Register(uint modifiers, uint virtualKey, Action onPressed)
    {
        var id = _nextId++;
        if (!User32.RegisterHotKey(_hwnd, id, modifiers | User32.MOD_NOREPEAT, virtualKey))
        {
            throw new InvalidOperationException("Не удалось зарегистрировать горячую клавишу — возможно, она уже занята другой программой.");
        }

        _handlers[id] = onPressed;
        return id;
    }

    public void Unregister(int id)
    {
        User32.UnregisterHotKey(_hwnd, id);
        _handlers.Remove(id);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == User32.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                try { action(); } catch { /* горячая клавиша не должна ронять приложение */ }
            }
        }

        return User32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _handlers.Keys.ToList())
        {
            User32.UnregisterHotKey(_hwnd, id);
        }
        _handlers.Clear();

        if (_subclassed)
        {
            User32.RemoveWindowSubclass(_hwnd, _proc, IntPtr.Zero);
        }
    }
}