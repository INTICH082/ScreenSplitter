namespace ScreenSplitter.Core.Models;

public class MonitorLayout
{
    public string MonitorId { get; init; } = "";
    public string Name { get; set; } = "Layout 1";
    public List<Zone> Zones { get; set; } = new();
}