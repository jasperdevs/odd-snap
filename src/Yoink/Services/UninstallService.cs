using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Yoink.Services;

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
        var shortcutPath = Path.Combine(programsDir, "Yoink.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "Yoink screenshot tool";
            shortcut.Save();
        }
        finally
        {
            try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    public static void RemoveStartMenuShortcut()
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Yoink.lnk");
        try
        {
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
        }
        catch { }
    }

    public static void RegisterInstalledAppEntry()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return;
        if (LooksLikeBuildOutputPath(exe))
            return;

        var installDir = GetInstallDirectory();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(4) ?? "1.0.0";
        var sizeKb = (int)Math.Max(1, new FileInfo(exe).Length / 1024);

        using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yoink");
        if (key is null) return;

        key.SetValue("DisplayName", "Yoink", RegistryValueKind.String);
        key.SetValue("DisplayVersion", version, RegistryValueKind.String);
        key.SetValue("Publisher", "Yoink Contributors", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", exe, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
    }

    public static void RemoveInstalledAppEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
        key?.DeleteSubKeyTree("Yoink", throwOnMissingSubKey: false);
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
        key?.DeleteValue("Yoink", throwOnMissingValue: false);
    }

    public static void RemoveAppData()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink");
        var legacyHistory = Path.Combine(appData, "history");

        try
        {
            if (Directory.Exists(legacyHistory))
                CopyDirectoryContents(legacyHistory, HistoryService.HistoryDir);
        }
        catch { }

        RemoveRuntimeCaches();
        TryDeleteDirectory(appData);
    }

    public static void RemoveRuntimeCaches()
    {
        TryDeleteDirectory(RembgRuntimeService.ModelCacheDirectory);
        TryDeleteDirectory(RembgRuntimeService.RootDirectory);
        TryDeleteDirectory(LocalClipRuntimeService.CacheDirectory);
    }

    public static void ScheduleInstallFolderRemoval()
    {
        var dir = GetInstallDirectory();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        var cmd = $"timeout /t 2 /nobreak >nul & rmdir /s /q \"{dir}\"";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { }
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
            catch { }
        }
    }

    private static bool LooksLikeBuildOutputPath(string path)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        return normalized.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\src\Yoink\bin\", StringComparison.OrdinalIgnoreCase);
    }
}
