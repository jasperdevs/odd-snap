using System.IO.Compression;
using System.Reflection;
using Xunit;
using Yoink.Services;

namespace Yoink.Tests;

public sealed class InstallServiceUpdateTests
{
    [Fact]
    public void ApplyUpdateFromZip_CopiesFilesIntoTargetDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoink-tests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(root, "package");
        var targetDir = Path.Combine(root, "target");
        var zipPath = Path.Combine(root, "update.zip");

        try
        {
            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(targetDir);

            File.WriteAllText(Path.Combine(packageDir, "Yoink.exe"), "new executable");
            File.WriteAllText(Path.Combine(packageDir, "portable.txt"), "portable");
            Directory.CreateDirectory(Path.Combine(packageDir, "nested"));
            File.WriteAllText(Path.Combine(packageDir, "nested", "data.txt"), "payload");

            File.WriteAllText(Path.Combine(targetDir, "Yoink.exe"), "old executable");

            ZipFile.CreateFromDirectory(packageDir, zipPath);

            InstallService.ApplyUpdateFromZip(zipPath, targetDir, launchAfter: false);

            Assert.Equal("new executable", File.ReadAllText(Path.Combine(targetDir, "Yoink.exe")));
            Assert.Equal("portable", File.ReadAllText(Path.Combine(targetDir, "portable.txt")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(targetDir, "nested", "data.txt")));
            Assert.False(File.Exists(zipPath));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void StandaloneInstallerFolder_DoesNotLookLikeFullPayloadTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoink-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Yoink.exe"), "installer");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "user file");

            Assert.False(InvokeShouldCopyFullPayloadTree(root));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void PublishPayloadFolder_LooksLikeFullPayloadTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoink-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Yoink.exe"), "installer");
            File.WriteAllText(Path.Combine(root, "ffmpeg.exe"), "binary");

            Assert.True(InvokeShouldCopyFullPayloadTree(root));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void OptionalPayloadEntries_IncludeBundledClipAssets()
    {
        var method = typeof(InstallService).GetMethod("GetOptionalPayloadEntries", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entries = Assert.IsAssignableFrom<IEnumerable<string>>(method!.Invoke(null, Array.Empty<object>()));

        Assert.Contains(Path.Combine("Assets", "Clip"), entries);
    }

    private static bool InvokeShouldCopyFullPayloadTree(string sourceDir)
    {
        var method = typeof(InstallService).GetMethod("ShouldCopyFullPayloadTree", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, new object[] { sourceDir }));
    }
}
