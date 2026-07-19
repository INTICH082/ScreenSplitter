using System.Diagnostics;
using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

public static class ProcessWindowLocator
{
    public static async Task<(Process? Process, IntPtr Handle)> LaunchAndWaitForWindowAsync(
        string target,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        var ownProcessId = (uint)Environment.ProcessId;

        Process? process;
        try
        {
            var startInfo = IsUrl(target) ? DefaultBrowserLauncher.BuildLaunchInfo(target) : new ProcessStartInfo(target) { UseShellExecute = true };

            process = Process.Start(startInfo);
        }
        catch
        {
            process = null;
        }

        if (process is not null)
        {
            var handle = await WaitForMainWindowAsync(process, TimeSpan.FromMilliseconds(1500));
            if (handle != IntPtr.Zero)
            {
                return (process, handle);
            }
        }

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(150);

            var foreground = User32.GetForegroundWindow();
            if (IsUsableWindow(foreground, ownProcessId))
            {
                return (process, foreground);
            }
        }

        return (process, IntPtr.Zero);
    }

    public static async Task<IntPtr> WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch (InvalidOperationException)
            {
                break;
            }

            await Task.Delay(150);
        }

        return IntPtr.Zero;
    }

    private static bool IsUrl(string target) =>
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        target.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsableWindow(IntPtr hwnd, uint ownProcessId)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindowVisible(hwnd)) return false;

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == ownProcessId) return false;

        return User32.GetWindowTextLength(hwnd) > 0;
    }
}
