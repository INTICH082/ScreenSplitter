using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class ZoneChipWindow : Window
{
    public event EventHandler? AssignRequested;

    public event EventHandler? ClearRequested;

    public event EventHandler? SwapClicked;

    public ZoneChipWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyNonActivatingStyle();
    }

    public void PlaceAt(PixelPoint topLeft)
    {
        Position = topLeft;
    }

    public void Render(ZoneSlotStatus status, string? title)
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
    }

    public void SetSelectedForSwap(bool selected)
    {
        ChipBorder.Classes.Set("swap-selected", selected);
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (isShift)
        {
            SwapClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (Label.Text == "+ Назначить")
        {
            AssignRequested?.Invoke(this, EventArgs.Empty);
        }
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