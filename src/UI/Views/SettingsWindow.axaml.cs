using Avalonia.Controls;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class SettingsWindow : Window
{
    private bool _initializing = true;

    public SettingsWindow()
    {
        InitializeComponent();
        StartupCheckBox.IsChecked = StartupManager.IsEnabled();

        var currentTheme = ThemePreference.Load();
        LightThemeRadio.IsChecked = currentTheme == "Light";
        DarkThemeRadio.IsChecked = currentTheme != "Light";

        _initializing = false;
    }

    private void OnStartupCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_initializing) return;
        StartupManager.SetEnabled(StartupCheckBox.IsChecked == true);
    }

    private void OnThemeChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_initializing) return;
        var theme = LightThemeRadio.IsChecked == true ? "Light" : "Dark";
        (Avalonia.Application.Current as App)?.SetTheme(theme);
    }
}