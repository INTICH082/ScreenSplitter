using System.Runtime.Versioning;
using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class SystemLoadMonitor
{
    private static long _lastIdle, _lastKernel, _lastUser;
    private static bool _hasSample;

    public static double GetCpuUsagePercent()
    {
        if (!Kernel32.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return -1;
        }

        var idleTicks = ToLong(idle);
        var kernelTicks = ToLong(kernel);
        var userTicks = ToLong(user);

        if (!_hasSample)
        {
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;
            _hasSample = true;
            return 0;
        }

        var idleDelta = idleTicks - _lastIdle;
        var kernelDelta = kernelTicks - _lastKernel;
        var userDelta = userTicks - _lastUser;

        _lastIdle = idleTicks;
        _lastKernel = kernelTicks;
        _lastUser = userTicks;

        var totalDelta = kernelDelta + userDelta;
        if (totalDelta <= 0) return 0;

        var busy = totalDelta - idleDelta;
        return Math.Clamp(busy * 100.0 / totalDelta, 0, 100);
    }

    private static long ToLong(Kernel32.FILETIME ft) =>
        ((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
}