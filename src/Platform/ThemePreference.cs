using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class ThemePreference
{
    private const string RegistryKeyPath = @"Software\ScreenSplitter";
    private const string ValueName = "Theme"; // "Dark" или "Light"

    /// <summary>Возвращает сохранённую тему, либо "Dark" по умолчанию, если ничего не сохранено.</summary>
    public static string Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return value == "Light" ? "Light" : "Dark";
        }
        catch
        {
            return "Dark";
        }
    }

    public static void Save(string themeName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(ValueName, themeName);
        }
        catch
        {
            // сохранение предпочтения темы не должно ронять приложение
        }
    }
}