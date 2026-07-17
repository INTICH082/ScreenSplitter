using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ScreenSplitter.UI.Views;

public enum AssignChoiceKind { Cancelled, Free, App }

public record AssignChoice(AssignChoiceKind Kind, string? AppPath = null);

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
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите приложение",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Исполняемые файлы") { Patterns = new[] { "*.exe" } },
                    new FilePickerFileType("Все файлы") { Patterns = new[] { "*.*" } }
                }
            });
        }
        finally
        {
            _pickerInProgress = false;
        }

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        TryComplete(path is not null
            ? new AssignChoice(AssignChoiceKind.App, path)
            : new AssignChoice(AssignChoiceKind.Cancelled));
    }

    private void TryComplete(AssignChoice choice)
    {
        _tcs.TrySetResult(choice);
    }
}