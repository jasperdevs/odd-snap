using System.Text.RegularExpressions;

namespace OddSnap.Services;

internal static partial class PythonLauncherSelector
{
    private static readonly string[] PreferredOnnxRuntimeVersions = ["3.12", "3.11", "3.10"];

    internal sealed record LauncherEntry(string LauncherArgument, string Version, string Path, bool IsDefault);

    public static IReadOnlyList<LauncherEntry> ParseLauncherListOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var entries = new List<LauncherEntry>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = LauncherListRegex().Match(rawLine);
            if (!match.Success)
                continue;

            var version = match.Groups["version"].Value;
            if (!IsSupportedVersionFormat(version))
                continue;

            entries.Add(new LauncherEntry(
                $"-{version}",
                version,
                match.Groups["path"].Value.Trim(),
                match.Groups["default"].Success));
        }

        return entries;
    }

    public static string? SelectOnnxRuntimeLauncherArgument(IEnumerable<LauncherEntry> entries)
    {
        var candidates = entries.ToList();
        foreach (var preferredVersion in PreferredOnnxRuntimeVersions)
        {
            var match = candidates.FirstOrDefault(entry => entry.Version.Equals(preferredVersion, StringComparison.Ordinal));
            if (match is not null)
                return match.LauncherArgument;
        }

        return null;
    }

    public static bool IsSupportedOnnxRuntimeVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
            return false;

        var match = PythonVersionRegex().Match(versionText);
        if (!match.Success)
            return false;

        return PreferredOnnxRuntimeVersions.Contains(match.Groups["version"].Value, StringComparer.Ordinal);
    }

    public static string BuildOnnxRuntimeMissingVersionMessage(IEnumerable<LauncherEntry> entries)
    {
        var discovered = entries
            .Select(entry => entry.Version)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(version => version, StringComparer.Ordinal)
            .ToList();

        return discovered.Count == 0
            ? "OddSnap needs Python 3.10, 3.11, or 3.12 installed to set up the local sticker and upscale runtimes."
            : $"OddSnap needs Python 3.10, 3.11, or 3.12 to set up the local sticker and upscale runtimes. Found: {string.Join(", ", discovered)}.";
    }

    private static bool IsSupportedVersionFormat(string version)
        => Regex.IsMatch(version, @"^\d+\.\d+$", RegexOptions.CultureInvariant);

    [GeneratedRegex(@"^\s*-V:(?<version>\d+\.\d+)(?:-[^\s]+)?\s*(?<default>\*)?\s+(?<path>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LauncherListRegex();

    [GeneratedRegex(@"(?<version>\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PythonVersionRegex();
}
