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

    [Fact]
    public void ApplyUpdateCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "InstallService.cs"));

        var fileCleanup = GetMethodBlock(source, "private static void TryDeleteFile(string path)");
        Assert.Contains("\"install.update-cleanup\"", fileCleanup);
        Assert.Contains("Failed to delete update package", fileCleanup);
        Assert.DoesNotContain("catch { }", fileCleanup);

        var directoryCleanup = GetMethodBlock(source, "private static void TryDeleteDirectory(string path)");
        Assert.Contains("\"install.update-cleanup\"", directoryCleanup);
        Assert.Contains("Failed to delete update extraction directory", directoryCleanup);
        Assert.DoesNotContain("catch { }", directoryCleanup);
    }

    [Fact]
    public void InstallerShortcutAndRegistrationFailuresAreDiagnosable()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "InstallService.cs"));

        var createShortcut = GetMethodBlock(source, "private static void CreateShortcut(string shortcutPath, string targetExe)");
        Assert.Contains("Windows shortcut service is unavailable.", createShortcut);
        Assert.DoesNotContain("catch { }\r\n    }", createShortcut);

        var installBlock = GetMethodBlock(source, "public static void Install(");
        Assert.Contains("\"install.start-menu-shortcut\"", installBlock);
        Assert.Contains("\"install.desktop-shortcut\"", installBlock);
        Assert.Contains("\"install.register-app\"", installBlock);

        var closeRunningBlock = GetMethodBlock(source, "public static void KillRunningInstances()");
        Assert.Contains("\"install.close-running-instance\"", closeRunningBlock);
        Assert.Contains("Failed to close running OddSnap process", closeRunningBlock);
        Assert.Contains("catch (Exception ex)", closeRunningBlock);
        Assert.DoesNotContain("catch { }", closeRunningBlock);

        var registerApp = GetMethodBlock(source, "private static void RegisterApp(string installDir, string exePath, string? versionLabel = null)");
        Assert.Contains("\"install.register-app\"", registerApp);
        Assert.Contains("Failed to register installed app metadata", registerApp);
        Assert.Contains("\"install.register-app-size\"", registerApp);
        Assert.Contains("Failed to read installed file size", registerApp);
        Assert.Contains("Failed to estimate installed app size", registerApp);
    }

    [Fact]
    public void LaunchInstalledReportsLaunchFailure()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "InstallService.cs"));

        var launchInstalled = GetMethodBlock(source, "public static void LaunchInstalled(string targetDir, bool showOnboarding)");
        Assert.Contains("if (!TryLaunch(targetExe, targetDir, args))", launchInstalled);
        Assert.Contains("OddSnap was installed, but the installed copy could not be launched.", launchInstalled);

        var tryLaunch = GetMethodBlock(source, "private static bool TryLaunch(string exePath, string workingDir, string args)");
        Assert.Contains("Exception? lastError = null;", tryLaunch);
        Assert.Contains("catch (Exception ex)", tryLaunch);
        Assert.Contains("lastError = ex;", tryLaunch);
        Assert.Contains("\"install.launch\"", tryLaunch);
        Assert.Contains("Failed to launch", tryLaunch);
        Assert.Contains("lastError", tryLaunch);
    }

    private static bool InvokeShouldCopyFullPayloadTree(string sourceDir)
    {
        var method = typeof(InstallService).GetMethod("ShouldCopyFullPayloadTree", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, new object[] { sourceDir }));
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
