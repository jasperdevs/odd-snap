using System.Drawing;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class CaptureOutputServiceTests
{
    [Theory]
    [InlineData(CaptureImageFormat.Png, "png")]
    [InlineData(CaptureImageFormat.Jpeg, "jpg")]
    [InlineData(CaptureImageFormat.Bmp, "bmp")]
    public void SaveBitmap_WritesRequestedFormatWithoutLeavingTempFiles(CaptureImageFormat format, string extension)
    {
        var root = CreateTempRoot();
        try
        {
            using var bitmap = new Bitmap(12, 8);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Coral);
            }

            var filePath = Path.Combine(root, "nested", $"capture.{extension}");
            CaptureOutputService.SaveBitmap(bitmap, filePath, format, jpegQuality: 90);

            Assert.True(File.Exists(filePath));
            Assert.NotEmpty(File.ReadAllBytes(filePath));
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(filePath)!), path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public void SaveBitmapToTempPng_CreatesReadablePng()
    {
        string? tempPath = null;
        try
        {
            using var bitmap = new Bitmap(10, 6);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.CadetBlue);
            }

            tempPath = CaptureOutputService.SaveBitmapToTempPng(bitmap, "oddsnap-test");

            Assert.NotNull(tempPath);
            Assert.EndsWith(".png", tempPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(tempPath));
            Assert.NotEmpty(File.ReadAllBytes(tempPath));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    [Fact]
    public void SavePng_WritesWithoutLeavingTempFiles()
    {
        var root = CreateTempRoot();
        try
        {
            using var bitmap = new Bitmap(10, 6);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.MediumSeaGreen);
            }

            var filePath = Path.Combine(root, "direct", "capture.png");
            CaptureOutputService.SavePng(bitmap, filePath);

            Assert.True(File.Exists(filePath));
            Assert.NotEmpty(File.ReadAllBytes(filePath));
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(filePath)!), path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public void TemporaryFileCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "CaptureOutputService.cs"));

        Assert.Contains("private static void TryDeleteTemporaryFile(string path, string context)", source);
        Assert.Contains("\"capture.output.cleanup\"", source);
        Assert.Contains("Failed to delete {context} temporary file", source);
        Assert.Contains("TryDeleteTemporaryFile(tempPath, \"failed temp PNG save\");", source);
        Assert.Contains("TryDeleteTemporaryFile(tmpPath, \"atomic write\");", source);
        Assert.DoesNotContain("try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }", source);
        Assert.DoesNotContain("try { File.Delete(tmpPath); } catch { }", source);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
