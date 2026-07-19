using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class ZoneSplitterWindow : Window
{
    public enum SplitterOrientation { Vertical, Horizontal }

    private readonly SplitterOrientation _orientation;
    private bool _dragging;
    private double _dragStartScreenCoord;

    public event Action? DragStarted;

    public event Action<double>? DragDelta;

    public event Action? DragEnded;

    public ZoneSplitterWindow() : this(SplitterOrientation.Vertical){}

    public ZoneSplitterWindow(SplitterOrientation orientation)
    {
        _orientation = orientation;
        InitializeComponent();

        Cursor = new Cursor(orientation == SplitterOrientation.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);

        Opened += (_, _) => ApplyNonActivating();
    }

    public void PlaceAt(PixelRect bounds)
    {
        Position = new PixelPoint(bounds.X, bounds.Y);
        var scaling = DesktopScaling > 0 ? DesktopScaling : 1.0;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
    }

    private PixelPoint GetScreenPoint(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new PixelPoint(Position.X + (int)(p.X * RenderScaling), Position.Y + (int)(p.Y * RenderScaling));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        var screen = GetScreenPoint(e);
        _dragStartScreenCoord = _orientation == SplitterOrientation.Vertical ? screen.X : screen.Y;

        e.Pointer.Capture(Handle);
        DragStarted?.Invoke();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var screen = GetScreenPoint(e);
        var current = _orientation == SplitterOrientation.Vertical ? screen.X : screen.Y;
        DragDelta?.Invoke(current - _dragStartScreenCoord);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        DragEnded?.Invoke();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        Line.Background = (Avalonia.Media.IBrush)this.FindResource("AccentSignal")!;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_dragging) return;
        Line.Background = (Avalonia.Media.IBrush)this.FindResource("LineHairline")!;
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