namespace ScreenSplitter.Core.Models;

public enum ZonePatternType
{
    Single,
    SplitVertical, // слева / справа
    SplitHorizontal, // сверху / снизу
    Grid2x2,
    ThreeColumns,
    AsymmetricGrid, // 1 большая зона + 3 меньшие, неравные пропорции
    Custom
}