using System.Diagnostics;
using System.Runtime.Versioning;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class GpuLoadMonitor
{
    private static List<PerformanceCounter>? _counters;
    private static bool _unavailable;

    public static double? GetGpuUsagePercent()
    {
        if (_unavailable) return null;

        EnsureCounters();
        if (_counters is null || _counters.Count == 0)
        {
            _unavailable = true;
            return null;
        }

        try
        {
            double sum = 0;
            foreach (var counter in _counters)
            {
                sum += counter.NextValue();
            }
            return Math.Clamp(sum, 0, 100);
        }
        catch
        {
            _unavailable = true;
            return null;
        }
    }

    private static void EnsureCounters()
    {
        if (_counters is not null) return;

        _counters = new List<PerformanceCounter>();
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            foreach (var instanceName in category.GetInstanceNames())
            {
                if (!instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var counter in category.GetCounters(instanceName))
                {
                    if (counter.CounterName == "Utilization Percentage")
                    {
                        _counters.Add(counter);
                    }
                }
            }
        }
        catch
        {
            _counters = new List<PerformanceCounter>();
        }
    }
}