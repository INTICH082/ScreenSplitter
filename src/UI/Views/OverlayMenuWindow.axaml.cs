using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class OverlayMenuWindow : Window
{
    private readonly ZoneManager _zoneManager;

    public OverlayMenuWindow(ZoneManager zoneManager)
    {
        _zoneManager = zoneManager;
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
        new ZonePatternPickerWindow(_zoneManager).Show();
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        new SettingsWindow().Show();
    }
}