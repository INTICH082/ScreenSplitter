using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class ZonePipMoveHandleWindow : Window
{
    private bool _dragging;
    private PixelPoint _dragStartScreen;

    public event Action? DragStarted;
    public event Action<double, double>? DragDelta; // (dx, dy) в экранных пикселях от начала перетаскивания
    public event Action? DragEnded;

    public ZonePipMoveHandleWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyNonActivating();
    }

    /// <summary>Позиционирует полосу-хват по верхнему краю зоны на всю её ширину.</summary>
    public void PlaceAt(PixelRect zoneBounds, double scaling)
    {
        var safeScaling = scaling > 0 ? scaling : 1.0;
        Position = new PixelPoint(zoneBounds.X, zoneBounds.Y);
        Width = zoneBounds.Width / safeScaling;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        _dragStartScreen = GetScreenPoint(e);
        e.Pointer.Capture(HandleBorder);
        DragStarted?.Invoke();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var current = GetScreenPoint(e);
        DragDelta?.Invoke(current.X - _dragStartScreen.X, current.Y - _dragStartScreen.Y);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        DragEnded?.Invoke();
    }

    private PixelPoint GetScreenPoint(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new PixelPoint(
            Position.X + (int)(p.X * RenderScaling),
            Position.Y + (int)(p.Y * RenderScaling));
    }

    private void ApplyNonActivating()
    {
        var handle = TryGetPlatformHandle();
        if (handle is not null && handle.Handle != IntPtr.Zero)
        {
            WindowStyleHelper.MakeNonActivating(handle.Handle);
        }
    }
}