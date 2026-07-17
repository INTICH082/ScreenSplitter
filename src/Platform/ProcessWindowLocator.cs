using System.Diagnostics;

namespace ScreenSplitter.Platform.Windows;

public static class ProcessWindowLocator
{
    public static async Task<(Process Process, IntPtr Handle)> LaunchAndWaitForWindowAsync(
        string executablePath,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Не удалось запустить приложение: {executablePath}");

        var handle = await WaitForMainWindowAsync(process, timeout ?? TimeSpan.FromSeconds(8));
        return (process, handle);
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
}