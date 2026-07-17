using Avalonia;

namespace ScreenSplitter.UI;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            TryEmergencyCleanup();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void TryEmergencyCleanup()
    {
        try
        {
            if (Application.Current is App app)
            {
                app.EmergencyShutdown();
            }
        }
        catch
        {
            // Если и аварийная очистка не сработала — ничего страшного, процесс всё равно завершается.
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}