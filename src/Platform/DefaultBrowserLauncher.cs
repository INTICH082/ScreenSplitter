using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class DefaultBrowserLauncher
{
    public static ProcessStartInfo BuildLaunchInfo(string url)
    {
        var browserExe = ResolveDefaultBrowserExe();
        var flag = browserExe is not null ? GetNewWindowFlag(browserExe) : null;

        if (browserExe is not null && flag is not null)
        {
            return new ProcessStartInfo(browserExe)
            {
                Arguments = $"{flag} \"{url}\"",
                UseShellExecute = true
            };
        }

        return new ProcessStartInfo(url) { UseShellExecute = true };
    }

    private static string? ResolveDefaultBrowserExe()
    {
        try
        {
            using var userChoiceKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = userChoiceKey?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return null;

            using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var command = commandKey?.GetValue(null) as string;
            if (string.IsNullOrEmpty(command)) return null;

            var match = Regex.Match(command, "\"([^\"]+\\.exe)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetNewWindowFlag(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        return name switch
        {
            "chrome" or "msedge" or "brave" or "vivaldi" or "opera" or "chromium" => "--new-window",
            "firefox" => "-new-window",
            _ => null
        };
    }
}