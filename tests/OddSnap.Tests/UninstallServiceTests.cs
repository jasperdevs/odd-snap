using Xunit;

namespace OddSnap.Tests;

public sealed class UninstallServiceTests
{
    [Fact]
    public void RuntimeCacheRemovalCoversAllLocalRuntimeRootsAndLogsCleanupFailures()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UninstallService.cs"));

        var removeRuntimeCachesBlock = GetMethodBlock(source, "public static void RemoveRuntimeCaches()");
        Assert.Contains("RembgRuntimeService.ModelCacheDirectory", removeRuntimeCachesBlock);
        Assert.Contains("RembgRuntimeService.RootDirectory", removeRuntimeCachesBlock);
        Assert.Contains("UpscaleRuntimeService.ModelCacheDirectory", removeRuntimeCachesBlock);
        Assert.Contains("UpscaleRuntimeService.RootDirectory", removeRuntimeCachesBlock);
        Assert.Contains("OpenSourceTranslationRuntimeService.RootDirectory", removeRuntimeCachesBlock);

        var deleteDirectoryBlock = GetMethodBlock(source, "private static void TryDeleteDirectory(string path)");
        Assert.Contains("\"uninstall.cleanup\"", deleteDirectoryBlock);
        Assert.Contains("Failed to delete uninstall cleanup directory", deleteDirectoryBlock);
        Assert.Contains("Path.GetFileName(path)", deleteDirectoryBlock);
        Assert.DoesNotContain("catch { }", deleteDirectoryBlock);
    }

    [Fact]
    public void UninstallShortcutAndFolderCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UninstallService.cs"));

        var shortcutBlock = GetMethodBlock(source, "public static void RemoveStartMenuShortcut()");
        Assert.Contains("\"uninstall.shortcut-cleanup\"", shortcutBlock);
        Assert.Contains("Failed to delete Start Menu shortcut", shortcutBlock);
        Assert.Contains("Path.GetFileName(shortcutPath)", shortcutBlock);
        Assert.DoesNotContain("catch { }", shortcutBlock);

        var folderRemovalBlock = GetMethodBlock(source, "public static void ScheduleInstallFolderRemoval()");
        Assert.Contains("\"uninstall.folder-removal\"", folderRemovalBlock);
        Assert.Contains("Failed to schedule install folder removal", folderRemovalBlock);
        Assert.Contains("Path.GetFileName(dir)", folderRemovalBlock);
        Assert.Contains("using var process = Process.Start(new ProcessStartInfo", folderRemovalBlock);
        Assert.Contains("if (process is null)", folderRemovalBlock);
        Assert.Contains("Windows did not start install folder removal", folderRemovalBlock);
        Assert.DoesNotContain("catch { }", folderRemovalBlock);
    }

    [Fact]
    public void UninstallLegacyHistoryMigrationFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UninstallService.cs"));

        var removeAppDataBlock = GetMethodBlock(source, "public static void RemoveAppData()");
        Assert.Contains("\"uninstall.history-migration\"", removeAppDataBlock);
        Assert.Contains("Failed to preserve legacy history before app data removal", removeAppDataBlock);
        Assert.Contains("catch (Exception ex)", removeAppDataBlock);
        Assert.DoesNotContain("catch { }", removeAppDataBlock);

        var copyBlock = GetMethodBlock(source, "private static void CopyDirectoryContents(string sourceDir, string destinationDir)");
        Assert.Contains("\"uninstall.history-migration\"", copyBlock);
        Assert.Contains("Failed to copy legacy history file", copyBlock);
        Assert.Contains("Path.GetFileName(file)", copyBlock);
        Assert.Contains("catch (Exception ex)", copyBlock);
        Assert.DoesNotContain("catch { }", copyBlock);
    }

    [Fact]
    public void UninstallRegistryCleanupReportsUnavailableParentKeys()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UninstallService.cs"));

        var installedEntryBlock = GetMethodBlock(source, "public static void RemoveInstalledAppEntry()");
        Assert.Contains("\"uninstall.registry-cleanup\"", installedEntryBlock);
        Assert.Contains("Windows uninstall registry parent key could not be opened.", installedEntryBlock);
        Assert.Contains("if (key is null)", installedEntryBlock);
        Assert.Contains("key.DeleteSubKeyTree(\"OddSnap\", throwOnMissingSubKey: false);", installedEntryBlock);
        Assert.DoesNotContain("key?.DeleteSubKeyTree", installedEntryBlock);

        var startupEntryBlock = GetMethodBlock(source, "public static void RemoveStartupEntry()");
        Assert.Contains("\"uninstall.startup-cleanup\"", startupEntryBlock);
        Assert.Contains("Windows startup registry key could not be opened.", startupEntryBlock);
        Assert.Contains("if (key is null)", startupEntryBlock);
        Assert.Contains("key.DeleteValue(\"OddSnap\", throwOnMissingValue: false);", startupEntryBlock);
        Assert.DoesNotContain("key?.DeleteValue", startupEntryBlock);
    }

    [Fact]
    public void StartupRegistryEntryUsesSharedDiagnosableWriter()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UninstallService.cs"));
        var lifecycleSource = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Lifecycle.cs"));

        var setStartupBlock = GetMethodBlock(source, "public static void SetStartupEntry(bool enabled)");
        Assert.Contains("Registry.CurrentUser.CreateSubKey(rk)", setStartupBlock);
        Assert.Contains("Registry.CurrentUser.OpenSubKey(rk, writable: true)", setStartupBlock);
        Assert.Contains("throw new InvalidOperationException(\"Windows startup registry key could not be opened.\");", setStartupBlock);
        Assert.Contains("throw new InvalidOperationException(\"OddSnap could not resolve its executable path for startup.\");", setStartupBlock);
        Assert.Contains("existingKey.DeleteValue(\"OddSnap\", throwOnMissingValue: false);", setStartupBlock);
        Assert.DoesNotContain("existingKey?.DeleteValue", setStartupBlock);
        Assert.Contains("key.SetValue(\"OddSnap\", $\"\\\"{exe}\\\"\", RegistryValueKind.String);", setStartupBlock);

        var syncStartupBlock = GetMethodBlock(lifecycleSource, "private static void SyncStartupRegistry(bool enabled)");
        Assert.Contains("UninstallService.SetStartupEntry(enabled);", syncStartupBlock);
        Assert.DoesNotContain("OpenSubKey", syncStartupBlock);
        Assert.DoesNotContain("SetValue", syncStartupBlock);
    }

    [Fact]
    public void StartupInstallMetadataRepairFailuresAreDiagnosable()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "UninstallService.cs"));

        var shortcutBlock = GetMethodBlock(source, "public static void EnsureStartMenuShortcut()");
        Assert.Contains("throw new InvalidOperationException(\"Windows shortcut service is unavailable.\");", shortcutBlock);
        Assert.DoesNotContain("if (shellType is null)\r\n            return;", shortcutBlock);

        var registerBlock = GetMethodBlock(source, "public static void RegisterInstalledAppEntry()");
        Assert.Contains("throw new InvalidOperationException(\"Windows uninstall registry key could not be opened.\");", registerBlock);
        Assert.Contains("TrySetEstimatedSize(key, installDir);", registerBlock);
        Assert.DoesNotContain("if (key is null) return;", registerBlock);
        Assert.DoesNotContain("catch { }", registerBlock);

        var estimatedSizeBlock = GetMethodBlock(source, "private static void TrySetEstimatedSize(RegistryKey key, string installDir)");
        Assert.Contains("\"startup.register-installed-entry.estimated-size\"", estimatedSizeBlock);
        Assert.Contains("Failed to read installed file size", estimatedSizeBlock);
        Assert.Contains("Failed to estimate installed app size", estimatedSizeBlock);
        Assert.DoesNotContain("catch { }", estimatedSizeBlock);
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
