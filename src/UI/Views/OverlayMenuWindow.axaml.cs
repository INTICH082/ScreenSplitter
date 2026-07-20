using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class OverlayMenuWindow : Window
{
    private readonly ZoneManager _zoneManager;
    private DispatcherTimer? _statsTimer;

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
        StartStatsTimer();
    }

    private void StartStatsTimer()
    {
        _statsTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1.5) };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();
    }

    private void UpdateStats()
    {
        var cpu = SystemLoadMonitor.GetCpuUsagePercent();
        CpuLabel.Text = cpu >= 0 ? $"CPU {cpu:0}%" : "CPU --";

        var gpu = GpuLoadMonitor.GetGpuUsagePercent();
        GpuLabel.Text = gpu is { } gpuValue ? $"GPU {gpuValue:0}%" : "GPU н/д";
    }

    private void PositionTopRight()
    {
        var area = Screens.Primary?.WorkingArea;
        if (area is { } workingArea)
        {
            Position = new PixelPoint(
                workingArea.X + workingArea.Width - (int)Width - 10, workingArea.Y + 10);
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

    private void OnScenariosClicked(object? sender, RoutedEventArgs e)
    {
        Action rebuildHotkeys = () => (Avalonia.Application.Current as App)?.RebuildProfileHotkeys();
        new ProfileManagerWindow(_zoneManager, rebuildHotkeys).Show();
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        new SettingsWindow().Show();
    }

    private const double FullWidth = 230, FullHeight = 112;
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

    private void OnCollapsedPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var positionBeforeDrag = Position;
        BeginMoveDrag(e); // блокирует выполнение до отпускания кнопки мыши

        // Если за время перетаскивания окно реально не сдвинулось — считаем это обычным кликом.
        if (Position == positionBeforeDrag)
        {
            ExpandPanel();
        }
    }

    private void ExpandPanel()
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