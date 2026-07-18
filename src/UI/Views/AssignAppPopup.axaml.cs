using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ScreenSplitter.UI.Views;

public enum AssignChoiceKind { Cancelled, Free, App }

public record AssignChoice(AssignChoiceKind Kind, string? AppPath = null, string? DisplayName = null);

public partial class AssignAppPopup : Window
{
    private readonly TaskCompletionSource<AssignChoice> _tcs = new();
    private bool _pickerInProgress;

    public AssignAppPopup()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!_pickerInProgress)
                TryComplete(new AssignChoice(AssignChoiceKind.Cancelled));
        };
    }

    public static async Task<AssignChoice> ShowAsync(Window owner, PixelPoint anchor)
    {
        var popup = new AssignAppPopup
        {
            Position = anchor
        };
        popup.Show(owner);
        var result = await popup._tcs.Task;
        popup.Close();
        return result;
    }

    private void OnFreeClicked(object? sender, RoutedEventArgs e)
    {
        TryComplete(new AssignChoice(AssignChoiceKind.Free));
    }

    private async void OnPickAppClicked(object? sender, RoutedEventArgs e)
    {
        _pickerInProgress = true;
        AssignChoice result;
        try
        {
            result = await QuickAppPickerWindow.ShowAsync(this, new PixelPoint(Position.X, Position.Y));
        }
        finally
        {
            _pickerInProgress = false;
        }

        TryComplete(result);
    }

    private void TryComplete(AssignChoice choice)
    {
        _tcs.TrySetResult(choice);
    }
}