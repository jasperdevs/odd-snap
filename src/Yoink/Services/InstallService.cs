using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Yoink.Services;

public static class InstallService
{
    public static string DefaultInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Yoink");

    /// <summary>Check if the app is running from a proper install location.</summary>
    public static bool IsInstalled()
    {
        try
        {
            if (LooksLikeBuildOutputPath(AppContext.BaseDirectory))
                return false;

            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yoink");
            if (key == null) return false;
            var installLoc = key.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(installLoc)) return false;
            var currentDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            return string.Equals(currentDir, installLoc.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Check if we should show the installer (not installed, not portable mode).</summary>
    public static bool ShouldShowInstaller()
    {
        if (LooksLikeBuildOutputPath(AppContext.BaseDirectory))
            return true;

        if (IsInstalled()) return false;
        // Portable mode: if a portable.txt file exists next to the exe, skip installer
        var portableFlag = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        if (File.Exists(portableFlag)) return false;
        return true;
    }

    /// <summary>Install Yoink to the target directory.</summary>
    public static void Install(string targetDir, bool desktopShortcut, bool startMenuShortcut, bool startWithWindows, Action<string>? onProgress = null)
    {
        onProgress?.Invoke("Creating directory...");
        Directory.CreateDirectory(targetDir);

        // Copy all files from current directory to target
        var sourceDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var targetDirNorm = targetDir.TrimEnd('\\', '/');

        if (string.Equals(sourceDir, targetDirNorm, StringComparison.OrdinalIgnoreCase))
            return; // already in the right place

        onProgress?.Invoke("Copying files...");
        CopyDirectory(sourceDir, targetDirNorm);

        var targetExe = Path.Combine(targetDirNorm, "Yoink.exe");

        // Start menu shortcut
        if (startMenuShortcut)
        {
            onProgress?.Invoke("Creating Start Menu shortcut...");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs", "Yoink.lnk"),
                targetExe);
        }

        // Desktop shortcut
        if (desktopShortcut)
        {
            onProgress?.Invoke("Creating desktop shortcut...");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Yoink.lnk"),
                targetExe);
        }

        // Register in Add/Remove Programs
        onProgress?.Invoke("Registering application...");
        RegisterApp(targetDirNorm, targetExe);

        // Startup registry
        if (startWithWindows)
        {
            const string rk = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.OpenSubKey(rk, true);
            key?.SetValue("Yoink", $"\"{targetExe}\"");
        }

        onProgress?.Invoke("Installation complete!");
    }

    /// <summary>Launch the installed copy and exit this process.</summary>
    public static void LaunchInstalled(string targetDir, bool showOnboarding)
    {
        var targetExe = Path.Combine(targetDir, "Yoink.exe");
        var args = showOnboarding ? "--post-install" : "";
        TryLaunch(targetExe, targetDir, args);
    }

    private static void TryLaunch(string exePath, string workingDir, string args)
    {
        const int attempts = 6;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    Thread.Sleep(150 * (i + 1));
                    continue;
                }

                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = workingDir,
                });

                if (proc != null)
                    return;
            }
            catch
            {
                Thread.Sleep(150 * (i + 1));
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destFile = Path.Combine(target, Path.GetFileName(file));
            try { File.Copy(file, destFile, true); }
            catch { } // skip locked files
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.Equals("runtimes", StringComparison.OrdinalIgnoreCase)
                || dirName.Equals("ref", StringComparison.OrdinalIgnoreCase))
            {
                CopyDirectory(dir, Path.Combine(target, dirName));
            }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetExe)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic sc = shell.CreateShortcut(shortcutPath);
                sc.TargetPath = targetExe;
                sc.WorkingDirectory = Path.GetDirectoryName(targetExe) ?? "";
                sc.IconLocation = targetExe + ",0";
                sc.Description = "Yoink screenshot tool";
                sc.Save();
            }
            finally { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { } }
        }
        catch { }
    }

    private static void RegisterApp(string installDir, string exePath)
    {
        try
        {
            if (LooksLikeBuildOutputPath(exePath))
                return;

            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yoink");
            if (key is null) return;
            key.SetValue("DisplayName", "Yoink");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Yoink Contributors");
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
        catch { }
    }

    private static bool LooksLikeBuildOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        return normalized.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\src\Yoink\bin\", StringComparison.OrdinalIgnoreCase);
    }
}
