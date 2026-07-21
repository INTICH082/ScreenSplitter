using System.Net.Http;
using System.Text.Json;

namespace ScreenSplitter.UI.Services;

/// Ненавязчивая проверка обновлений через GitHub Releases API.
public static class UpdateChecker
{
    private const string GitHubRepo = "INTICH082/ScreenSplitter";

    public record UpdateInfo(string TagName, string HtmlUrl);

    public static async Task<UpdateInfo?> CheckForNewerVersionAsync(Version currentVersion)
    {
        if (GitHubRepo.StartsWith("INTICH082")) return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenSplitter-UpdateChecker");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            using var doc = JsonDocument.Parse(json);

            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            var url = doc.RootElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
            if (tag is null) return null;

            var cleanTag = tag.TrimStart('v', 'V');
            if (Version.TryParse(cleanTag, out var remoteVersion) && remoteVersion > currentVersion)
            {
                return new UpdateInfo(tag, url ?? $"https://github.com/{GitHubRepo}/releases/latest");
            }
        }
        catch
        {
            // тихо игнорируем — нет сети, 404 (нет релизов), rate limit и т.п.
        }

        return null;
    }
}