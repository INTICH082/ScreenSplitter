using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class OverlayMenuWindow : Window
{
    private readonly ZoneManager _zoneManager;

    public OverlayMenuWindow() : this(new ZoneManager())
    {
    }

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
            Position = new PixelPoint(workingArea.X + workingArea.Width - (int)Width - 10, workingArea.Y + 10);
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

    private const double FullWidth = 182, FullHeight = 86;
    private const double CollapsedSize = 38;

    private void OnTaskbarToggleClicked(object? sender, RoutedEventArgs e)
    {
        TaskbarController.Toggle();
        ToolTip.SetTip(TaskbarButton, TaskbarController.IsHidden ? "Показать панель задач" : "Скрыть панель задач");
        _zoneManager.RecomputeLayout();
    }

    private void OnCollapseClicked(object? sender, RoutedEventArgs e)
    {
        FullPanel.IsVisible = false;
        CollapsedButton.IsVisible = true;
        Width = CollapsedSize;
        Height = CollapsedSize;
    }

    private void OnExpandClicked(object? sender, RoutedEventArgs e)
    {
        CollapsedButton.IsVisible = false;
        FullPanel.IsVisible = true;
        Width = FullWidth;
        Height = FullHeight;
    }

    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}