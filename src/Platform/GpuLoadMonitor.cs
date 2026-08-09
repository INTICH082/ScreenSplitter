using System.Diagnostics;
using System.Runtime.Versioning;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class GpuLoadMonitor
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private static List<PerformanceCounter>? _counters;
    private static DateTime _lastRefresh = DateTime.MinValue;

    public static double? GetGpuUsagePercent()
    {
        EnsureCountersFresh();
        if (_counters is null || _counters.Count == 0) return null;

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
            // Один из счётчиков мог исчезнуть (закрылось GPU-приложение) — не отключаем мониторинг
            // насовсем, просто пересоберём список счётчиков при следующем обращении.
            DisposeCounters();
            return null;
        }
    }

    private static void EnsureCountersFresh()
    {
        if (_counters is not null && DateTime.UtcNow - _lastRefresh < RefreshInterval) return;

        DisposeCounters();
        _lastRefresh = DateTime.UtcNow;
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
                    else
                    {
                        counter.Dispose(); // не используем этот конкретный счётчик — сразу освобождаем
                    }
                }
            }
        }
        catch
        {
            // Счётчики недоступны на этой системе — оставляем пустой список, попробуем ещё раз
            // через RefreshInterval (вдруг ситуация изменится, например обновится видеодрайвер).
        }
    }

    private static void DisposeCounters()
    {
        if (_counters is null) return;
        foreach (var counter in _counters) counter.Dispose();
        _counters = null;
    }
}