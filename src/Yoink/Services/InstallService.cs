using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace Yoink.Services;

public static class InstallService
{
    public static string DefaultInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Yoink");

    public static string GetRunningAppDirectory() => GetAppDirectory();

    public static string GetPreferredUpdateTargetDirectory()
    {
        var runningDir = GetRunningAppDirectory();
        var installedLocation = GetInstalledLocation();
        return ResolveUpdateTargetDirectory(installedLocation, runningDir, IsInstalled());
    }

    public static string? GetInstalledLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yoink");
            var installLoc = key?.GetValue("InstallLocation") as string;
            return string.IsNullOrWhiteSpace(installLoc) ? null : installLoc;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsInstalledLocation(string targetDir)
    {
        var installedLocation = GetInstalledLocation();
        if (string.IsNullOrWhiteSpace(installedLocation))
            return false;

        return string.Equals(
            targetDir.TrimEnd('\\', '/'),
            installedLocation.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAppDirectory()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
            return Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;

        return AppContext.BaseDirectory;
    }

    /// <summary>Check if the app is running from a proper install location.</summary>
    public static bool IsInstalled()
    {
        try
        {
            var appDir = GetAppDirectory();
            if (LooksLikeBuildOutputPath(appDir))
                return false;
            var installLoc = GetInstalledLocation();
            if (string.IsNullOrWhiteSpace(installLoc))
                return false;

            // Stale registry entry pointing to a removed directory should not count
            if (!Directory.Exists(installLoc))
                return false;

            var currentDir = appDir.TrimEnd('\\', '/');
            return string.Equals(currentDir, installLoc.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Check if we should show the installer.</summary>
    public static bool ShouldShowInstaller()
    {
        // Never show installer for debug/release builds — devs run from Visual Studio.
        if (LooksLikeBuildOutputPath(GetAppDirectory()))
            return false;

        // Running from the installed location — no installer needed.
        if (IsInstalled())
            return false;

        // Everything else (downloaded exe, random folder, etc.) should show the installer.
        return true;
    }

    /// <summary>Kill any running Yoink processes (other than this one).</summary>
    public static void KillRunningInstances()
    {
        var currentPid = Environment.ProcessId;
        foreach (var proc in Process.GetProcessesByName("Yoink"))
        {
            if (proc.Id == currentPid) continue;
            try
            {
                if (!proc.HasExited && proc.CloseMainWindow())
                    proc.WaitForExit(5000);

                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                }
            }
            catch { }
        }
    }

    /// <summary>Install Yoink to the target directory.</summary>
    public static void Install(
        string targetDir,
        bool desktopShortcut,
        bool startMenuShortcut,
        bool startWithWindows,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        targetDir = NormalizeTargetDirectory(targetDir);

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke("Closing any running Yoink instances...");
        KillRunningInstances();

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke("Creating directory...");
        Directory.CreateDirectory(targetDir);

        var targetDirNorm = targetDir.TrimEnd('\\', '/');

        onProgress?.Invoke("Copying files...");

        var sourceDir = GetAppDirectory();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            throw new InvalidOperationException("Unable to locate the running Yoink application folder.");
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            throw new InvalidOperationException("Unable to locate the running Yoink executable.");

        cancellationToken.ThrowIfCancellationRequested();
        var targetExe = Path.Combine(targetDirNorm, "Yoink.exe");

        if (!string.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\', '/'), Path.GetFullPath(targetDirNorm).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            CopyInstallPayload(sourceDir, currentExe, targetDirNorm, cancellationToken);

        // Start menu shortcut
        if (startMenuShortcut)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke("Creating Start Menu shortcut...");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs", "Yoink.lnk"),
                targetExe);
        }

        // Desktop shortcut
        if (desktopShortcut)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke("Creating desktop shortcut...");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Yoink.lnk"),
                targetExe);
        }

        // Register in Add/Remove Programs
        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke("Registering application...");
        RegisterApp(targetDirNorm, targetExe);

        // Startup registry
        if (startWithWindows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string rk = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.OpenSubKey(rk, true);
            key?.SetValue("Yoink", $"\"{targetExe}\"");
        }

        onProgress?.Invoke("Installation complete!");
    }

    /// <summary>Launch the installed copy and exit this process.</summary>
    public static void LaunchInstalled(string targetDir, bool showOnboarding)
    {
        targetDir = NormalizeTargetDirectory(targetDir);
        var targetExe = Path.Combine(targetDir, "Yoink.exe");
        var args = showOnboarding ? "--post-install" : "";
        TryLaunch(targetExe, targetDir, args);
    }

    public static void ApplyUpdateFromZip(string packagePath, string targetDir, string? versionLabel = null, bool launchAfter = true, Action<string>? onProgress = null)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));
        targetDir = NormalizeTargetDirectory(targetDir);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Update package not found.", packagePath);

        var targetDirNorm = targetDir.TrimEnd('\\', '/');
        var sourceRoot = Path.Combine(Path.GetTempPath(), "Yoink", "ApplyUpdate", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(sourceRoot);
            onProgress?.Invoke("Waiting for Yoink to close...");
            WaitForFileUnlocks(targetDirNorm);

            onProgress?.Invoke("Extracting update...");
            ZipFile.ExtractToDirectory(packagePath, sourceRoot, overwriteFiles: true);

            var extractedRoot = ResolveUpdateSourceRoot(sourceRoot);
            var installedTarget = IsInstalledLocation(targetDirNorm);

            onProgress?.Invoke("Copying update files...");
            CopyTree(extractedRoot, targetDirNorm);

            var targetExe = Path.Combine(targetDirNorm, "Yoink.exe");
            if (installedTarget)
            {
                onProgress?.Invoke("Refreshing app registration...");
                RegisterApp(targetDirNorm, targetExe, versionLabel);
            }

            if (launchAfter)
            {
                onProgress?.Invoke("Launching updated Yoink...");
                if (!TryLaunch(targetExe, targetDirNorm, ""))
                    throw new InvalidOperationException("The update was applied, but Yoink could not be restarted.");
            }
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
            TryDeleteFile(packagePath);
        }
    }

    private static bool TryLaunch(string exePath, string workingDir, string args)
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
                    return true;
            }
            catch
            {
                Thread.Sleep(150 * (i + 1));
            }
        }

        return false;
    }

    private static string ResolveUpdateTargetDirectory(string? installedLocation, string runningAppDirectory, bool runningInstalledCopy)
    {
        if (runningInstalledCopy && !string.IsNullOrWhiteSpace(installedLocation))
            return NormalizeTargetDirectory(installedLocation);

        return NormalizeTargetDirectory(runningAppDirectory);
    }

    private static string NormalizeTargetDirectory(string? targetDir)
    {
        var candidate = string.IsNullOrWhiteSpace(targetDir) ? DefaultInstallPath : targetDir.Trim();
        return Path.GetFullPath(candidate);
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

    private static void CopyTree(string source, string target, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(source, file);

            var destination = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyFileWithRetry(file, destination, cancellationToken);
        }
    }

    private static void CopyInstallPayload(string sourceDir, string currentExe, string targetDir, CancellationToken cancellationToken)
    {
        if (ShouldCopyFullPayloadTree(sourceDir))
        {
            CopyTree(sourceDir, targetDir, cancellationToken);
            return;
        }

        CopyFileWithRetry(currentExe, Path.Combine(targetDir, "Yoink.exe"), cancellationToken);

        foreach (var relativePath in GetOptionalPayloadEntries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(sourceDir, relativePath);
            var destinationPath = Path.Combine(targetDir, relativePath);

            if (File.Exists(sourcePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                CopyFileWithRetry(sourcePath, destinationPath, cancellationToken);
            }
            else if (Directory.Exists(sourcePath))
            {
                CopyTree(sourcePath, destinationPath, cancellationToken);
            }
        }
    }

    private static bool ShouldCopyFullPayloadTree(string sourceDir)
    {
        if (LooksLikeBuildOutputPath(sourceDir))
            return true;

        var entries = Directory.EnumerateFileSystemEntries(sourceDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (entries.Length == 0)
            return false;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Yoink.exe",
            "ffmpeg.exe"
        };

        return !entries.All(name => allowed.Contains(name!));
    }

    private static IEnumerable<string> GetOptionalPayloadEntries()
    {
        yield return "ffmpeg.exe";
        yield return Path.Combine("Assets", "Clip");
    }

    private static void CopyFileWithRetry(string source, string destination, CancellationToken cancellationToken = default)
    {
        const int attempts = 8;
        Exception? lastError = null;
        for (int i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Copy(source, destination, true);
                return;
            }
            catch (Exception ex) when (i < attempts - 1)
            {
                lastError = ex;
                cancellationToken.WaitHandle.WaitOne(200 * (i + 1));
            }
        }

        throw new IOException($"Failed to copy '{source}' to '{destination}'.", lastError);
    }

    private static string ResolveUpdateSourceRoot(string extractedRoot)
    {
        var directExe = Path.Combine(extractedRoot, "Yoink.exe");
        if (File.Exists(directExe))
            return extractedRoot;

        var childDirs = Directory.EnumerateDirectories(extractedRoot).ToList();
        if (childDirs.Count == 1 && File.Exists(Path.Combine(childDirs[0], "Yoink.exe")))
            return childDirs[0];

        var exe = Directory.EnumerateFiles(extractedRoot, "Yoink.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(exe))
            return Path.GetDirectoryName(exe) ?? extractedRoot;

        return extractedRoot;
    }

    private static void WaitForFileUnlocks(string targetDir)
    {
        var targetExe = Path.Combine(targetDir, "Yoink.exe");
        Exception? lastError = null;
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (!File.Exists(targetExe))
                    return;

                using var stream = new FileStream(targetExe, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(200);
            }
        }

        throw new IOException($"Timed out waiting for '{targetExe}' to close.", lastError);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
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

    private static void RegisterApp(string installDir, string exePath, string? versionLabel = null)
    {
        try
        {
            if (LooksLikeBuildOutputPath(exePath))
                return;

            string version;
            if (!string.IsNullOrWhiteSpace(versionLabel))
            {
                version = versionLabel.Trim().TrimStart('v', 'V');
            }
            else
            {
                var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                version = v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
            }
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yoink");
            if (key is null) return;
            key.SetValue("DisplayName", "Yoink");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "jasperdevs");
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("URLInfoAbout", "https://github.com/jasperdevs/yoink");
            key.SetValue("URLUpdateInfo", "https://github.com/jasperdevs/yoink/releases/latest");
            key.SetValue("HelpLink", "https://github.com/jasperdevs/yoink/issues");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            try
            {
                long totalBytes = 0;
                foreach (var f in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories))
                    try { totalBytes += new FileInfo(f).Length; } catch { }
                key.SetValue("EstimatedSize", (int)Math.Max(1, totalBytes / 1024), RegistryValueKind.DWord);
            }
            catch { }
        }
        catch { }
    }

    internal static bool LooksLikeBuildOutputPath(string path)
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
