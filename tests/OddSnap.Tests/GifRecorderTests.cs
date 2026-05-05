using Xunit;

namespace OddSnap.Tests;

public sealed class GifRecorderTests
{
    [Fact]
    public void GifRecorderTempCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Capture", "GifRecorder.cs"));

        var tempFileCleanup = GetMethodBlock(source, "private static void TryDeleteTempFile(string? path, string context)");
        Assert.Contains("\"gif.temp-cleanup\"", tempFileCleanup);
        Assert.Contains("Failed to delete {context} temporary file", tempFileCleanup);

        var tempDirectoryCleanup = GetMethodBlock(source, "private static void TryDeleteTempDirectory(string path)");
        Assert.Contains("\"gif.temp-cleanup\"", tempDirectoryCleanup);
        Assert.Contains("Failed to delete GIF temporary directory", tempDirectoryCleanup);

        Assert.Contains("TryDeleteTempFile(outputPath, \"failed GIF output\");", source);
        Assert.Contains("TryDeleteTempDirectory(_tempDir);", source);
        Assert.DoesNotContain("try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }", source);
        Assert.DoesNotContain("try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }", source);
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"Could not find method body for: {signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
                depth--;

            if (depth == 0)
                return source[braceStart..(i + 1)];
        }

        throw new InvalidOperationException($"Could not parse method block: {signature}");
    }
}
