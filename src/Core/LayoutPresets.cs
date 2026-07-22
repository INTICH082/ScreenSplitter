using ScreenSplitter.Core.Models;

namespace ScreenSplitter.Core;

public static class LayoutPresets
{
    public static IReadOnlyList<RelativeZoneRect> GetPattern(ZonePatternType type) => type switch
    {
        ZonePatternType.Single => new[]
        {
            new RelativeZoneRect(0, 0, 1, 1)
        },
        ZonePatternType.SplitVertical => new[]
        {
            new RelativeZoneRect(0, 0, 0.5, 1),
            new RelativeZoneRect(0.5, 0, 0.5, 1)
        },
        ZonePatternType.SplitHorizontal => new[]
        {
            new RelativeZoneRect(0, 0, 1, 0.5),
            new RelativeZoneRect(0, 0.5, 1, 0.5)
        },
        ZonePatternType.Grid2x2 => BuildGrid(2, 2),
        ZonePatternType.ThreeColumns => BuildGrid(3, 1),
        ZonePatternType.AsymmetricGrid => new[]
        {
            new RelativeZoneRect(0, 0, 0.65, 0.65),
            new RelativeZoneRect(0.65, 0, 0.35, 0.65),
            new RelativeZoneRect(0, 0.65, 0.65, 0.35),
            new RelativeZoneRect(0.65, 0.65, 0.35, 0.35)
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Для Custom используйте BuildGrid(cols, rows).")
    };

    public static IReadOnlyList<RelativeZoneRect> BuildGrid(int cols, int rows)
    {
        if (cols < 1 || rows < 1)
            throw new ArgumentException("Количество колонок и строк должно быть не меньше 1.");

        var zones = new List<RelativeZoneRect>(cols * rows);
        double w = 1.0 / cols;
        double h = 1.0 / rows;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                zones.Add(new RelativeZoneRect(c * w, r * h, w, h));
            }
        }

        return zones;
    }
}