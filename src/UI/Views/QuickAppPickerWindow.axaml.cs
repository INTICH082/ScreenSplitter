using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class QuickAppPickerWindow : Window
{
    private readonly TaskCompletionSource<AssignChoice> _tcs = new();
    private bool _pickerInProgress;

    public QuickAppPickerWindow()
    {
        InitializeComponent();
        PopulateList();

        Deactivated += (_, _) =>
        {
            if (!_pickerInProgress)
                TryComplete(new AssignChoice(AssignChoiceKind.Cancelled));
        };
    }

    public static async Task<AssignChoice> ShowAsync(Window owner, PixelPoint anchor)
    {
        var popup = new QuickAppPickerWindow
        {
            Position = anchor
        };
        popup.Show(owner);
        var result = await popup._tcs.Task;
        popup.Close();
        return result;
    }

    private void PopulateList()
    {
        foreach (var app in KnownAppsCatalog.GetAvailable())
        {
            var button = new Button
            {
                Content = app.Name,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            button.Classes.Add("action");
            button.Click += (_, _) => TryComplete(new AssignChoice(AssignChoiceKind.App, app.Target, app.Name));

            ItemsPanel.Children.Add(button);
        }
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
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