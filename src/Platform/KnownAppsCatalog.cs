using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ScreenSplitter.Platform.Windows;

public record KnownApp(string Name, string Target, bool IsUrl);

[SupportedOSPlatform("windows")]
public static class KnownAppsCatalog
{
    public static IReadOnlyList<KnownApp> GetAvailable()
    {
        var apps = new List<KnownApp>();

        AddIfFound(apps, "Блокнот", ResolveFromSystemRoot("notepad.exe"));
        AddIfFound(apps, "Проводник", ResolveFromSystemRoot("explorer.exe"));
        AddIfFound(apps, "Калькулятор", ResolveFromSystemRoot("calc.exe"));
        AddIfFound(apps, "Steam", ResolveSteamPath());
        AddIfFound(apps, "Telegram", ResolveTelegramPath());
        AddIfFound(apps, "Discord", ResolveDiscordPath());

        apps.Add(new KnownApp("Браузер (новая вкладка)", "about:blank", true));
        apps.Add(new KnownApp("YouTube", "https://youtube.com", true));
        apps.Add(new KnownApp("VK", "https://vk.com", true));

        return apps;
    }

    private static void AddIfFound(List<KnownApp> list, string name, string? path)
    {
        if (path is not null)
        {
            list.Add(new KnownApp(name, path, false));
        }
    }

    private static string? ResolveFromSystemRoot(string exeName)
    {
        try
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var full = Path.Combine(systemDir, exeName);
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSteamPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");

            if (key?.GetValue("InstallPath") as string is { } installPath)
            {
                var exe = Path.Combine(installPath, "steam.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch
        {
            // игнорируем — попробуем стандартные пути ниже
        }

        foreach (var candidate in new[]
        {
            @"C:\Program Files (x86)\Steam\steam.exe",
            @"C:\Program Files\Steam\steam.exe"
        })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? ResolveTelegramPath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidate = Path.Combine(appData, "Telegram Desktop", "Telegram.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveDiscordPath()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var discordDir = Path.Combine(localAppData, "Discord");
            if (!Directory.Exists(discordDir)) return null;

            var appFolder = Directory.GetDirectories(discordDir, "app-*")
                .OrderByDescending(d => d)
                .FirstOrDefault();
            if (appFolder is null) return null;

            var exe = Path.Combine(appFolder, "Discord.exe");
            return File.Exists(exe) ? exe : null;
        }
        catch
        {
            return null;
        }
    }
}