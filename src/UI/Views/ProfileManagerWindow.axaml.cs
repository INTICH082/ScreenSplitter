using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ScreenSplitter.Core.Models;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class ProfileManagerWindow : Window
{
    private readonly ZoneManager _zoneManager;
    private readonly Action _onProfilesChanged;
    private List<Profile> _profiles;

    public ProfileManagerWindow() : this(new ZoneManager(), () => { })
    {
    }

    public ProfileManagerWindow(ZoneManager zoneManager, Action onProfilesChanged)
    {
        InitializeComponent();
        _zoneManager = zoneManager;
        _onProfilesChanged = onProfilesChanged;
        _profiles = ProfileStore.LoadAll();
        RenderList();
    }

    private void RenderList()
    {
        ProfilesPanel.Children.Clear();

        if (_profiles.Count == 0)
        {
            ProfilesPanel.Children.Add(new TextBlock
            {
                Text = "Пока нет сохранённых сценариев.",
                Foreground = (Avalonia.Media.IBrush)this.FindResource("TextMuted")!,
                FontSize = 11.5
            });
            return;
        }

        foreach (var profile in _profiles)
        {
            ProfilesPanel.Children.Add(BuildRow(profile));
        }
    }

    private Control BuildRow(Profile profile)
    {
        var root = new Border
        {
            Background = (Avalonia.Media.IBrush)this.FindResource("BgPanelAlt")!,
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(10, 8)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto")
        };

        var nameBlock = new TextBlock
        {
            Text = $"{profile.Name}  ({profile.Cols}x{profile.Rows})",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameBlock, 0);

        var hotkeyBox = new ComboBox
        {
            Width = 92,
            Margin = new Avalonia.Thickness(6, 0),
            ItemsSource = new[] { "Без хоткея", "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3", "Ctrl+Alt+4", "Ctrl+Alt+5", "Ctrl+Alt+6", "Ctrl+Alt+7", "Ctrl+Alt+8", "Ctrl+Alt+9" },
            SelectedIndex = profile.HotkeyDigit ?? 0
        };
        hotkeyBox.SelectionChanged += (_, _) => OnHotkeyChanged(profile, hotkeyBox.SelectedIndex);
        Grid.SetColumn(hotkeyBox, 1);

        var applyButton = new Button { Content = "▶", Classes = { "action" }, Padding = new Avalonia.Thickness(8, 4) };
        ToolTip.SetTip(applyButton, "Применить сценарий");
        applyButton.Click += async (_, _) => await _zoneManager.ApplyProfileAsync(profile);
        Grid.SetColumn(applyButton, 2);

        var deleteButton = new Button { Content = "✕", Classes = { "action" }, Margin = new Avalonia.Thickness(6, 0, 0, 0), Padding = new Avalonia.Thickness(8, 4) };
        ToolTip.SetTip(deleteButton, "Удалить сценарий");
        deleteButton.Click += (_, _) => OnDeleteClicked(profile);
        Grid.SetColumn(deleteButton, 3);

        grid.Children.Add(nameBlock);
        grid.Children.Add(hotkeyBox);
        grid.Children.Add(applyButton);
        grid.Children.Add(deleteButton);

        root.Child = grid;
        return root;
    }

    private void OnHotkeyChanged(Profile profile, int selectedIndex)
    {
        var digit = selectedIndex == 0 ? (int?)null : selectedIndex;

        // Хоткей может принадлежать только одному сценарию — освобождаем его у остальных.
        if (digit is not null)
        {
            foreach (var other in _profiles)
            {
                if (!ReferenceEquals(other, profile) && other.HotkeyDigit == digit)
                {
                    other.HotkeyDigit = null;
                }
            }
        }

        profile.HotkeyDigit = digit;
        ProfileStore.SaveAll(_profiles);
        _onProfilesChanged();
        RenderList();
    }

    private void OnDeleteClicked(Profile profile)
    {
        _profiles.Remove(profile);
        ProfileStore.SaveAll(_profiles);
        _onProfilesChanged();
        RenderList();
    }

    private void OnSaveCurrentClicked(object? sender, RoutedEventArgs e)
    {
        var name = NewProfileNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var profile = _zoneManager.CaptureCurrentAsProfile(name);
        if (profile is null)
        {
            HintLabel.Text = "Сначала разбей экран на зоны (кнопка «▦») — сохранять пока нечего.";
            return;
        }

        _profiles.RemoveAll(p => p.Name == name);
        _profiles.Add(profile);
        ProfileStore.SaveAll(_profiles);
        _onProfilesChanged();

        NewProfileNameBox.Text = "";
        RenderList();
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (_profiles.Count == 0)
        {
            HintLabel.Text = "Пока нечего экспортировать — нет сохранённых сценариев.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт сценариев ScreenSplitter",
            SuggestedFileName = "screensplitter-scenarios.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        if (file is null) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_profiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(json);
            HintLabel.Text = $"Экспортировано сценариев: {_profiles.Count}.";
        }
        catch
        {
            HintLabel.Text = "Не удалось сохранить файл экспорта.";
        }
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт сценариев ScreenSplitter",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new System.IO.StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var imported = System.Text.Json.JsonSerializer.Deserialize<List<Profile>>(json);
            if (imported is null || imported.Count == 0)
            {
                HintLabel.Text = "Файл не содержит сценариев.";
                return;
            }

            // Сценарии с совпадающим именем перезаписываются импортированной версией.
            foreach (var profile in imported)
            {
                _profiles.RemoveAll(p => p.Name == profile.Name);
                _profiles.Add(profile);
            }

            ProfileStore.SaveAll(_profiles);
            _onProfilesChanged();
            HintLabel.Text = $"Импортировано сценариев: {imported.Count}.";
            RenderList();
        }
        catch
        {
            HintLabel.Text = "Не удалось прочитать файл — проверь, что это корректный экспорт ScreenSplitter.";
        }
    }
}