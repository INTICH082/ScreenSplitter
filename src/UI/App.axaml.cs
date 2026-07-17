using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScreenSplitter.UI.Views;

namespace ScreenSplitter.UI;

public partial class App : Application
{
    private OverlayMenuWindow? _overlayWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _overlayWindow = new OverlayMenuWindow();
            _overlayWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnToggleOverlay(object? sender, System.EventArgs e)
    {
        if (_overlayWindow is null) return;
        _overlayWindow.IsVisible = !_overlayWindow.IsVisible;
    }

    private void OnExit(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}