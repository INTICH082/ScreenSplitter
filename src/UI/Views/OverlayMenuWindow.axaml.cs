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
    private const double FullWidth = 272, FullHeight = 140;
    private const double CollapsedSize = 38;
    private const double ClickThreshold = 4;

    private readonly ZoneManager _zoneManager;
    private DispatcherTimer? _statsTimer;
    private string? _updateUrl;

    // --- Ручное перетаскивание (не полагаемся на BeginMoveDrag — ведёт себя нестабильно) ---
    private bool _dragging;
    private PixelPoint _dragStartScreen;
    private PixelPoint _dragWindowStart;
    private double _dragDistance;

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

    private void OnPipToggleClicked(object? sender, RoutedEventArgs e)
    {
        _zoneManager.TogglePictureInPicture();
        PipButton.Classes.Set("active", _zoneManager.HasPictureInPicture);
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        new SettingsWindow().Show();
    }

    private void OnTaskbarToggleClicked(object? sender, RoutedEventArgs e)
    {
        TaskbarController.Toggle();
        TaskbarButton.Classes.Set("active", TaskbarController.IsHidden);
        ToolTip.SetTip(TaskbarButton, TaskbarController.IsHidden ? "Показать панель задач" : "Скрыть панель задач");
        _zoneManager.RecomputeLayout();
    }

    /// <summary>Показывает ненавязчивое уведомление о доступной новой версии в панели.</summary>
    public void ShowUpdateAvailable(string tagName, string url)
    {
        _updateUrl = url;
        UpdateNoticeLabel.Text = $"Доступна {tagName}";
        UpdateNotice.IsVisible = true;
    }

    private void OnUpdateNoticeClicked(object? sender, PointerPressedEventArgs e)
    {
        if (_updateUrl is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updateUrl) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    private void OnCollapseClicked(object? sender, RoutedEventArgs e)
    {
        FullPanel.IsVisible = false;
        CollapsedButton.IsVisible = true;
        Width = CollapsedSize;
        Height = CollapsedSize;
    }

    private void ExpandPanel()
    {
        CollapsedButton.IsVisible = false;
        FullPanel.IsVisible = true;
        Width = FullWidth;
        Height = FullHeight;
    }

    // --- Общая логика перетаскивания для хвата полной панели и для свёрнутой кнопки ---

    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e) => BeginDrag(e);

    private void OnCollapsedPointerPressed(object? sender, PointerPressedEventArgs e) => BeginDrag(e);

    private void BeginDrag(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Avalonia.Input.IInputElement el) return;

        _dragging = true;
        _dragDistance = 0;
        _dragStartScreen = GetScreenPoint(e);
        _dragWindowStart = Position;
        e.Pointer.Capture(el);
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var current = GetScreenPoint(e);
        var dx = current.X - _dragStartScreen.X;
        var dy = current.Y - _dragStartScreen.Y;
        _dragDistance = System.Math.Max(_dragDistance, System.Math.Sqrt(dx * dx + dy * dy));

        Position = new PixelPoint(_dragWindowStart.X + dx, _dragWindowStart.Y + dy);
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);

        // Если мышь почти не сдвинулась — считаем это кликом. На свёрнутой кнопке это раскрывает панель;
        // на хвате полной панели клик ничего не делает (это просто пустая область).
        if (_dragDistance < ClickThreshold && CollapsedButton.IsVisible)
        {
            ExpandPanel();
        }
    }

    private PixelPoint GetScreenPoint(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new PixelPoint(
            Position.X + (int)(p.X * RenderScaling),
            Position.Y + (int)(p.Y * RenderScaling));
    }
}