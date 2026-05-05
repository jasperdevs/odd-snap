using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OddSnap.Services;

public static class UninstallService
{
    public static void EnsureStartMenuShortcut()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return;
        if (LooksLikeBuildOutputPath(exe))
            return;

        var programsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(programsDir);
        var shortcutPath = Path.Combine(programsDir, "OddSnap.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("Windows shortcut service is unavailable.");

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "OddSnap screenshot tool";
            shortcut.Save();
        }
        finally
        {
            try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    public static void RemoveStartMenuShortcut()
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "OddSnap.lnk");
        try
        {
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "uninstall.shortcut-cleanup",
                $"Failed to delete Start Menu shortcut {Path.GetFileName(shortcutPath)}: {ex.Message}",
                ex);
        }
    }

    public static void RegisterInstalledAppEntry()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return;
        if (LooksLikeBuildOutputPath(exe))
            return;

        var installDir = GetInstallDirectory();
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        var version = v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OddSnap");
        if (key is null)
            throw new InvalidOperationException("Windows uninstall registry key could not be opened.");

        key.SetValue("DisplayName", "OddSnap", RegistryValueKind.String);
        key.SetValue("DisplayVersion", version, RegistryValueKind.String);
        key.SetValue("Publisher", "jasperdevs", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", exe, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall", RegistryValueKind.String);
        key.SetValue("URLInfoAbout", "https://github.com/jasperdevs/odd-snap", RegistryValueKind.String);
        key.SetValue("URLUpdateInfo", "https://github.com/jasperdevs/odd-snap/releases/latest", RegistryValueKind.String);
        key.SetValue("HelpLink", "https://github.com/jasperdevs/odd-snap/issues", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
        TrySetEstimatedSize(key, installDir);
    }

    public static void RemoveInstalledAppEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
        if (key is null)
        {
            AppDiagnostics.LogWarning(
                "uninstall.registry-cleanup",
                "Windows uninstall registry parent key could not be opened.");
            return;
        }

        key.DeleteSubKeyTree("OddSnap", throwOnMissingSubKey: false);
    }

    public static string GetInstallDirectory()
    {
        var exe = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(exe) ? "" : Path.GetDirectoryName(exe) ?? "";
    }

    public static void RemoveStartupEntry()
    {
        const string rk = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(rk, writable: true);
        if (key is null)
        {
            AppDiagnostics.LogWarning(
                "uninstall.startup-cleanup",
                "Windows startup registry key could not be opened.");
            return;
        }

        key.DeleteValue("OddSnap", throwOnMissingValue: false);
    }

    public static void SetStartupEntry(bool enabled)
    {
        const string rk = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        if (!enabled)
        {
            using var existingKey = Registry.CurrentUser.OpenSubKey(rk, writable: true);
            if (existingKey is null)
                throw new InvalidOperationException("Windows startup registry key could not be opened.");

            existingKey.DeleteValue("OddSnap", throwOnMissingValue: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(rk);
        if (key is null)
            throw new InvalidOperationException("Windows startup registry key could not be opened.");

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("OddSnap could not resolve its executable path for startup.");

        key.SetValue("OddSnap", $"\"{exe}\"", RegistryValueKind.String);
    }

    public static void RemoveAppData()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OddSnap");
        var legacyHistory = Path.Combine(appData, "history");

        try
        {
            if (Directory.Exists(legacyHistory))
                CopyDirectoryContents(legacyHistory, HistoryService.HistoryDir);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "uninstall.history-migration",
                $"Failed to preserve legacy history before app data removal: {ex.Message}",
                ex);
        }

        RemoveRuntimeCaches();
        TryDeleteDirectory(appData);
    }

    public static void RemoveRuntimeCaches()
    {
        TryDeleteDirectory(RembgRuntimeService.ModelCacheDirectory);
        TryDeleteDirectory(RembgRuntimeService.RootDirectory);
        TryDeleteDirectory(UpscaleRuntimeService.ModelCacheDirectory);
        TryDeleteDirectory(UpscaleRuntimeService.RootDirectory);
        TryDeleteDirectory(OpenSourceTranslationRuntimeService.RootDirectory);
    }

    public static void ScheduleInstallFolderRemoval()
    {
        var dir = GetInstallDirectory();
        if (!IsSafeInstallDirectoryForRemoval(dir))
            return;

        var cmd = $"timeout /t 2 /nobreak >nul & rmdir /s /q \"{dir}\"";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null)
                AppDiagnostics.LogWarning(
                    "uninstall.folder-removal",
                    $"Windows did not start install folder removal for {Path.GetFileName(dir)}.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "uninstall.folder-removal",
                $"Failed to schedule install folder removal for {Path.GetFileName(dir)}: {ex.Message}",
                ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "uninstall.cleanup",
                $"Failed to delete uninstall cleanup directory {Path.GetFileName(path)}: {ex.Message}",
                ex);
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destinationDir, relative);
            var destFolder = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destFolder))
                Directory.CreateDirectory(destFolder);

            try
            {
                File.Copy(file, destPath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "uninstall.history-migration",
                    $"Failed to copy legacy history file {Path.GetFileName(file)}: {ex.Message}",
                    ex);
            }
        }
    }

    private static bool LooksLikeBuildOutputPath(string path) => InstallService.LooksLikeBuildOutputPath(path);

    private static void TrySetEstimatedSize(RegistryKey key, string installDir)
    {
        try
        {
            long totalBytes = 0;
            foreach (var file in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    totalBytes += new FileInfo(file).Length;
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogWarning(
                        "startup.register-installed-entry.estimated-size",
                        $"Failed to read installed file size for {Path.GetFileName(file)}: {ex.Message}",
                        ex);
                }
            }

            key.SetValue("EstimatedSize", (int)Math.Max(1, totalBytes / 1024), RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "startup.register-installed-entry.estimated-size",
                $"Failed to estimate installed app size for {Path.GetFileName(installDir)}: {ex.Message}",
                ex);
        }
    }

    private static bool IsSafeInstallDirectoryForRemoval(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return false;

        var fullPath = Path.GetFullPath(dir);
        if (LooksLikeBuildOutputPath(fullPath))
            return false;

        var expectedRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "OddSnap"));

        return string.Equals(fullPath, expectedRoot, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(Path.Combine(fullPath, "OddSnap.exe"));
    }
}
