using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public sealed class WindowMoveWatcher : IDisposable
{
    private readonly User32.WinEventDelegate _callback;
    private readonly IntPtr _hook;
    private readonly uint _ownProcessId;

    public event Action<IntPtr>? MoveStarted;
    public event Action<IntPtr>? MoveEnded;

    public WindowMoveWatcher()
    {
        _ownProcessId = (uint)Environment.ProcessId;
        _callback = OnWinEvent;

        _hook = User32.SetWinEventHook(
            User32.EVENT_SYSTEM_MOVESIZESTART,
            User32.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != User32.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownProcessId) return;

        if (eventType == User32.EVENT_SYSTEM_MOVESIZESTART)
        {
            MoveStarted?.Invoke(hwnd);
        }
        else if (eventType == User32.EVENT_SYSTEM_MOVESIZEEND)
        {
            MoveEnded?.Invoke(hwnd);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_hook);
        }
    }
}