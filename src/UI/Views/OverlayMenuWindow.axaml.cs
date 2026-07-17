using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class OverlayMenuWindow : Window
{
    public OverlayMenuWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, System.EventArgs e)
    {
        PositionTopRight();
        ApplyNoActivateStyle();
    }

    private void PositionTopRight()
    {
        var area = Screens.Primary?.WorkingArea;
        if (area is { } workingArea)
        {
            Position = new PixelPoint(
                workingArea.X + workingArea.Width - (int)Width - 10,
                workingArea.Y + 10);
        }
    }

    private void ApplyNoActivateStyle()
    {
        var handle = TryGetPlatformHandle();
        if (handle is not null && handle.Handle != System.IntPtr.Zero)
        {
            WindowStyleHelper.MakeNonActivating(handle.Handle);
        }
    }

    private void OnZonesClicked(object? sender, RoutedEventArgs e)
    {
        // Заглушка. На этапе 2 здесь будет открываться ZoneEditorWindow
        // с выбором пресета раскладки (2 колонки / сетка 2x2 / и т.д.)
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        // Заглушка под будущее окно настроек.
    }
}