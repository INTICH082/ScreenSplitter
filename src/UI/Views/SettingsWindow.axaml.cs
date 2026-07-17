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
        _initializing = false;
    }

    private void OnStartupCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_initializing) return;
        StartupManager.SetEnabled(StartupCheckBox.IsChecked == true);
    }
}