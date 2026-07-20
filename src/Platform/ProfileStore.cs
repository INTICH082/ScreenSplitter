using System.Runtime.Versioning;
using System.Text.Json;
using ScreenSplitter.Core.Models;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string GetFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenSplitter");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "profiles.json");
    }

    public static List<Profile> LoadAll()
    {
        try
        {
            var path = GetFilePath();
            if (!File.Exists(path)) return new List<Profile>();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<Profile>>(json, JsonOptions) ?? new List<Profile>();
        }
        catch
        {
            return new List<Profile>();
        }
    }

    public static void SaveAll(IEnumerable<Profile> profiles)
    {
        try
        {
            var json = JsonSerializer.Serialize(profiles.ToList(), JsonOptions);
            File.WriteAllText(GetFilePath(), json);
        }
        catch
        {
            // сохранение сценариев не должно ронять приложение
        }
    }
}