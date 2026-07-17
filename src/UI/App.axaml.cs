using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScreenSplitter.Core.Models;
using ScreenSplitter.UI.Services;
using ScreenSplitter.UI.Views;

namespace ScreenSplitter.UI;

public partial class App : Application
{
    private OverlayMenuWindow? _overlayWindow;
    private readonly ZoneManager _zoneManager = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _overlayWindow = new OverlayMenuWindow(_zoneManager);
            _zoneManager.AttachScreenSource(_overlayWindow);
            _overlayWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnToggleOverlay(object? sender, System.EventArgs e)
    {
        if (_overlayWindow is null) return;
        _overlayWindow.IsVisible = !_overlayWindow.IsVisible;
    }

    private void OnPatternSingleClicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.Single);

    private void OnPatternVerticalClicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.SplitVertical);

    private void OnPatternHorizontalClicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.SplitHorizontal);

    private void OnPatternGrid2x2Clicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.Grid2x2);

    private void OnPatternCustomClicked(object? sender, System.EventArgs e)
    {
        new ZonePatternPickerWindow(_zoneManager).Show();
    }

    private void OnSettingsMenuClicked(object? sender, System.EventArgs e)
    {
        new SettingsWindow().Show();
    }

    private void OnExit(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}