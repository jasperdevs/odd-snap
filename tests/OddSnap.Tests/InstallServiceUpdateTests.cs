using System.IO.Compression;
using System.Reflection;
using Xunit;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class InstallServiceUpdateTests
{
    [Fact]
    public void Install_WhenCancelledBeforeStart_DoesNotCreateTargetDirectory()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "oddsnap-tests", Guid.NewGuid().ToString("N"), "target");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            InstallService.Install(
                targetDir,
                desktopShortcut: false,
                startMenuShortcut: false,
                startWithWindows: false,
                cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public void ApplyUpdateFromZip_CopiesFilesIntoTargetDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(root, "package");
        var targetDir = Path.Combine(root, "target");
        var zipPath = Path.Combine(root, "update.zip");

        try
        {
            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(targetDir);

            File.WriteAllText(Path.Combine(packageDir, "OddSnap.exe"), "new executable");
            File.WriteAllText(Path.Combine(packageDir, "portable.txt"), "portable");
            Directory.CreateDirectory(Path.Combine(packageDir, "nested"));
            File.WriteAllText(Path.Combine(packageDir, "nested", "data.txt"), "payload");

            File.WriteAllText(Path.Combine(targetDir, "OddSnap.exe"), "old executable");

            ZipFile.CreateFromDirectory(packageDir, zipPath);

            InstallService.ApplyUpdateFromZip(zipPath, targetDir, launchAfter: false);

            Assert.Equal("new executable", File.ReadAllText(Path.Combine(targetDir, "OddSnap.exe")));
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
    public void InstallerOnlyFolder_DoesNotLookLikeFullPayloadTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "OddSnap.exe"), "installer");

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
    public void FolderWithExtraPayloadEntries_LooksLikeFullPayloadTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "oddsnap-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "OddSnap.exe"), "installer");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "extra payload");

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
    public void OptionalPayloadEntries_DoNotIncludeBundledClipAssets()
    {
        var method = typeof(InstallService).GetMethod("GetOptionalPayloadEntries", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entries = Assert.IsAssignableFrom<IEnumerable<string>>(method!.Invoke(null, Array.Empty<object>()));

        Assert.DoesNotContain(Path.Combine("Assets", "Clip"), entries);
    }

    [Theory]
    [InlineData("C:\\Installed\\OddSnap", "C:\\Portable\\OddSnap", true, "C:\\Installed\\OddSnap")]
    [InlineData("C:\\Installed\\OddSnap", "C:\\Portable\\OddSnap", false, "C:\\Portable\\OddSnap")]
    [InlineData(null, "C:\\Portable\\OddSnap", false, "C:\\Portable\\OddSnap")]
    [InlineData(null, "", false, null)]
    public void ResolveUpdateTargetDirectory_PrefersInstalledPathOnlyWhenRunningInstalledCopy(string? installedLocation, string runningDir, bool runningInstalledCopy, string? expected)
    {
        var method = typeof(InstallService).GetMethod("ResolveUpdateTargetDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var actual = Assert.IsType<string>(method!.Invoke(null, new object?[] { installedLocation, runningDir, runningInstalledCopy }));

        if (expected is null)
            Assert.Equal(InstallService.DefaultInstallPath, actual);
        else
            Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeTargetDirectory_FallsBackToDefaultWhenBlank(string? input)
    {
        var method = typeof(InstallService).GetMethod("NormalizeTargetDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var actual = Assert.IsType<string>(method!.Invoke(null, new object?[] { input }));

        Assert.Equal(Path.GetFullPath(InstallService.DefaultInstallPath), actual);
    }

    private static bool InvokeShouldCopyFullPayloadTree(string sourceDir)
    {
        var method = typeof(InstallService).GetMethod("ShouldCopyFullPayloadTree", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, new object[] { sourceDir }));
    }
}
