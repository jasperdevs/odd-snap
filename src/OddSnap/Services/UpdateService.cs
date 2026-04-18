using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OddSnap.Services;

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version? LatestVersion,
    string LatestVersionLabel,
    string ReleaseUrl,
    string? DownloadUrl,
    string? AssetName,
    string? AssetSha256,
    DateTimeOffset? PublishedAt,
    bool IsUpdateAvailable,
    string StatusMessage);

public static class UpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/jasperdevs/odd-snap/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/jasperdevs/odd-snap/releases/latest";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private static UpdateCheckResult? _cachedResult;
    private static DateTimeOffset _cachedAt;

    public static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    public static string GetCurrentVersionLabel()
    {
        var v = GetCurrentVersion();
        // Show 3-part "v0.6.2" unless revision is non-zero
        return v.Revision > 0 ? $"v{v}" : $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
        {
            var cached = _cachedResult;
            if (cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
                return cached;
        }

        await CheckGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh)
            {
                var cached = _cachedResult;
                if (cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
                    return cached;
            }

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
            var asset = PickBestUpdateAsset(release.Assets);
            var isUpdateAvailable = latestVersion > currentVersion;
            var status = isUpdateAvailable
                ? $"Update available: {latestLabel} (current {GetCurrentVersionLabel()})"
                : $"You're up to date on {GetCurrentVersionLabel()}";

            var result = new UpdateCheckResult(
                currentVersion,
                latestVersion,
                latestLabel,
                releaseUrl,
                asset?.BrowserDownloadUrl,
                asset?.Name,
                TryExtractSha256Hex(asset?.Digest),
                release.PublishedAt,
                isUpdateAvailable,
                status);

            _cachedResult = result;
            _cachedAt = DateTimeOffset.UtcNow;
            return result;
        }
        finally
        {
            CheckGate.Release();
        }
    }

    private static string? TryExtractSha256Hex(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return null;

        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = digest[prefix.Length..].Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            return null;

        return hash.ToUpperInvariant();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"OddSnap/{GetCurrentVersionLabel()}");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static Version ParseVersion(string? tagName)
    {
        var raw = (tagName ?? string.Empty).Trim();
        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            raw = raw[1..];

        var match = Regex.Match(raw, @"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<build>\d+))?(?:\.(?<rev>\d+))?");
        if (match.Success)
        {
            int major = int.Parse(match.Groups["major"].Value);
            int minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : 0;
            int build = match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value) : 0;
            int revision = match.Groups["rev"].Success ? int.Parse(match.Groups["rev"].Value) : 0;
            return new Version(major, minor, build, revision);
        }

        return new Version(0, 0, 0);
    }

    private static GitHubAsset? PickBestUpdateAsset(IReadOnlyList<GitHubAsset>? assets)
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

        static bool IsZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        static bool IsInstaller(string name) =>
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase);

        return assets.FirstOrDefault(asset =>
                   IsZip(asset.Name) &&
                   asset.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset => IsZip(asset.Name))
               ?? assets.FirstOrDefault(asset =>
                   IsInstaller(asset.Name) &&
                   asset.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset => IsInstaller(asset.Name));
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

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
