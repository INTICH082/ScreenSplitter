using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
        switch (status)
        {
            case ZoneSlotStatus.Empty:
                Label.Text = "+ Назначить";
                ClearButton.IsVisible = false;
                ChipBorder.Background = new SolidColorBrush(Color.Parse("#DD1E1E1E"));
                break;
            case ZoneSlotStatus.Free:
                Label.Text = "Свободно";
                ClearButton.IsVisible = true;
                ChipBorder.Background = new SolidColorBrush(Color.Parse("#DD2E4E2E"));
                break;
            case ZoneSlotStatus.Assigned:
                Label.Text = title ?? "Приложение";
                ClearButton.IsVisible = true;
                ChipBorder.Background = new SolidColorBrush(Color.Parse("#DD1E2E4E"));
                break;
        }
    }

    public void SetSelectedForSwap(bool selected)
    {
        ChipBorder.BorderBrush = selected ? new SolidColorBrush(Color.Parse("#FFFFA500")) : null;
        ChipBorder.BorderThickness = selected ? new Thickness(2) : new Thickness(0);
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