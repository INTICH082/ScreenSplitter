using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class ZoneChipWindow : Window
{
    public event EventHandler? AssignRequested;

    public event EventHandler? ClearRequested;

    public event EventHandler? SwapClicked;

    public event Action<PixelPoint>? Moved;

    private bool _draggable;
    private bool _dragging;
    private PixelPoint _dragStartScreen;
    private PixelPoint _dragWindowStart;
    private double _dragDistance;

    public ZoneChipWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyNonActivatingStyle();
    }

    public void EnableDragging()
    {
        _draggable = true;
    }

    public void PlaceAt(PixelPoint topLeft)
    {
        Position = topLeft;
    }

    public void Render(ZoneSlotStatus status, string? title, byte[]? iconPngBytes = null)
    {
        ChipBorder.Classes.Set("free", status == ZoneSlotStatus.Free);
        ChipBorder.Classes.Set("assigned", status == ZoneSlotStatus.Assigned);
        Classes.Set("compact", status == ZoneSlotStatus.Assigned);

        Label.Text = status switch
        {
            ZoneSlotStatus.Empty => "+ Назначить",
            ZoneSlotStatus.Free => "Свободно",
            ZoneSlotStatus.Assigned => title ?? "Приложение",
            _ => "?"
        };

        ClearButton.IsVisible = status != ZoneSlotStatus.Empty;

        if (iconPngBytes is not null)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(iconPngBytes);
                AppIcon.Source = new Bitmap(stream);
                AppIcon.IsVisible = true;
                StatusDot.IsVisible = false;
                return;
            }
            catch
            {
                // если декодировать не удалось — просто покажем точку-индикатор вместо иконки
            }
        }

        AppIcon.IsVisible = false;
        StatusDot.IsVisible = true;
    }

    public void SetSelectedForSwap(bool selected)
    {
        ChipBorder.Classes.Set("swap-selected", selected);
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (_draggable && !isShift && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = true;
            _dragDistance = 0;
            _dragStartScreen = GetScreenPoint(e);
            _dragWindowStart = Position;
            e.Pointer.Capture(ChipBorder);
            return;
        }

        if (isShift)
        {
            SwapClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (Label.Text == "+ Назначить")
        {
            AssignRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnChipPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var current = GetScreenPoint(e);
        var dx = current.X - _dragStartScreen.X;
        var dy = current.Y - _dragStartScreen.Y;
        _dragDistance = Math.Max(_dragDistance, Math.Sqrt(dx * dx + dy * dy));

        var newPos = new PixelPoint(_dragWindowStart.X + dx, _dragWindowStart.Y + dy);
        Position = newPos;
        Moved?.Invoke(newPos);
    }

    private void OnChipPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);

        // Если мышь почти не сдвинулась — считаем это обычным кликом (назначить), а не перетаскиванием.
        if (_dragDistance < 4 && Label.Text == "+ Назначить")
        {
            AssignRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private PixelPoint GetScreenPoint(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new PixelPoint(
            Position.X + (int)(p.X * RenderScaling),
            Position.Y + (int)(p.Y * RenderScaling));
    }

    private void OnClearClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyNonActivatingStyle()
    {
        var handle = TryGetPlatformHandle();
        if (handle is not null && handle.Handle != IntPtr.Zero)
        {
            WindowStyleHelper.MakeNonActivating(handle.Handle);
        }
    }
}