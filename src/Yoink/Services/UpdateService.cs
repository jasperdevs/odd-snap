using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yoink.Services;

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version? LatestVersion,
    string LatestVersionLabel,
    string ReleaseUrl,
    string? DownloadUrl,
    string? AssetName,
    DateTimeOffset? PublishedAt,
    bool IsUpdateAvailable,
    string StatusMessage);

public static class UpdateService
{
    private const string Owner = "jasperdevs";
    private const string Repo = "yoink";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/jasperdevs/yoink/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/jasperdevs/yoink/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    public static string GetCurrentVersionLabel() => $"v{GetCurrentVersion()}";

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub returned an empty release response.");

        var latestVersion = ParseVersion(release.TagName);
        var latestLabel = string.IsNullOrWhiteSpace(release.TagName) ? $"v{latestVersion}" : release.TagName.Trim();
        var releaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl;
        var asset = PickBestAsset(release.Assets);
        var isUpdateAvailable = latestVersion > currentVersion;
        var status = isUpdateAvailable
            ? $"Update available: {latestLabel}"
            : $"You're up to date on {GetCurrentVersionLabel()}";

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            latestLabel,
            releaseUrl,
            asset?.BrowserDownloadUrl,
            asset?.Name,
            release.PublishedAt,
            isUpdateAvailable,
            status);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Yoink/{GetCurrentVersion()}");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    private static Version ParseVersion(string? tagName)
    {
        var raw = (tagName ?? string.Empty).Trim();
        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            raw = raw[1..];

        if (Version.TryParse(raw, out var version))
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        return new Version(0, 0, 0);
    }

    private static GitHubAsset? PickBestAsset(IReadOnlyList<GitHubAsset>? assets)
    {
        if (assets is not { Count: > 0 })
            return null;

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };

        return assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                   && asset.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && asset.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault();
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
