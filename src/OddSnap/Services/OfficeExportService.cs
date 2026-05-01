using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using OddSnap.Models;

namespace OddSnap.Services;

public enum OfficeExportTarget
{
    Word,
    PowerPoint,
    Excel
}

public sealed record OpenWithAppSuggestion(string Name, string Path);

public static class OfficeExportService
{
    private static readonly Lazy<IReadOnlyList<OpenWithAppSuggestion>> AppSuggestions = new(LoadAppSuggestions);

    public static IReadOnlyList<string> SupportedOpenWithExtensions { get; } =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".mp4",
        ".webm",
        ".mkv"
    ];

    public static IReadOnlyList<OfficeExportTarget> Targets { get; } =
    [
        OfficeExportTarget.Word,
        OfficeExportTarget.PowerPoint,
        OfficeExportTarget.Excel
    ];

    public static IEnumerable<OfficeExportTarget> GetInstalledTargets()
        => Targets.Where(IsTargetInstalled);

    public static string GetTargetName(OfficeExportTarget target) => target switch
    {
        OfficeExportTarget.PowerPoint => "PowerPoint",
        OfficeExportTarget.Excel => "Excel",
        _ => "Word"
    };

    public static string GetProgId(OfficeExportTarget target) => target switch
    {
        OfficeExportTarget.PowerPoint => "PowerPoint.Application",
        OfficeExportTarget.Excel => "Excel.Application",
        _ => "Word.Application"
    };

    public static bool IsTargetInstalled(OfficeExportTarget target)
        => Type.GetTypeFromProgID(GetProgId(target)) is not null;

    public static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
            normalized = "." + normalized;

        return SupportedOpenWithExtensions.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    public static string GetOpenWithLabel(string extension)
        => extension.TrimStart('.').ToUpperInvariant();

    public static IReadOnlyList<OpenWithAppSuggestion> GetOpenWithSuggestions(string? query, int maxCount = 6)
    {
        var normalizedQuery = (query ?? "").Trim();
        var suggestions = AppSuggestions.Value;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return suggestions.Take(maxCount).ToList();

        return suggestions
            .Select(s => new
            {
                Suggestion = s,
                Score = GetSuggestionScore(s, normalizedQuery)
            })
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Suggestion.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(x => x.Suggestion)
            .ToList();
    }

    public static bool TryGetConfiguredApp(AppSettings settings, string? extension, out string appPath)
    {
        appPath = "";
        var normalized = NormalizeExtension(extension);
        if (normalized is null || settings.OpenWithApps is null)
            return false;

        if (!settings.OpenWithApps.TryGetValue(normalized, out var configured) ||
            string.IsNullOrWhiteSpace(configured) ||
            !File.Exists(configured))
        {
            return false;
        }

        appPath = configured;
        return true;
    }

    public static void SaveConfiguredApp(AppSettings settings, string extension, string appPath)
    {
        var normalized = NormalizeExtension(extension)
            ?? throw new InvalidOperationException("Unsupported file type.");

        settings.OpenWithApps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.OpenWithApps[normalized] = NormalizeAppPath(appPath);
    }

    public static void ClearConfiguredApp(AppSettings settings, string extension)
    {
        var normalized = NormalizeExtension(extension);
        if (normalized is null || settings.OpenWithApps is null)
            return;

        settings.OpenWithApps.Remove(normalized);
    }

    public static void OpenFileWithApp(string imagePath, string appPath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = NormalizeAppPath(appPath),
            Arguments = QuoteArgument(imagePath),
            UseShellExecute = true
        });
    }

    public static string NormalizeAppPath(string appPath)
    {
        var trimmed = appPath.Trim().Trim('"');
        return Environment.ExpandEnvironmentVariables(trimmed);
    }

    private static IReadOnlyList<OpenWithAppSuggestion> LoadAppSuggestions()
    {
        var suggestions = new Dictionary<string, OpenWithAppSuggestion>(StringComparer.OrdinalIgnoreCase);
        AddStartMenuSuggestions(suggestions, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        AddStartMenuSuggestions(suggestions, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
        AddExecutableSuggestions(suggestions, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddExecutableSuggestions(suggestions, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        return suggestions.Values
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(250)
            .ToList();
    }

    private static void AddStartMenuSuggestions(Dictionary<string, OpenWithAppSuggestion> suggestions, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        foreach (var path in EnumerateFilesSafe(folder, "*.lnk", 4).Take(180))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            suggestions.TryAdd(path, new OpenWithAppSuggestion(name, path));
        }
    }

    private static void AddExecutableSuggestions(Dictionary<string, OpenWithAppSuggestion> suggestions, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        foreach (var path in EnumerateFilesSafe(folder, "*.exe", 3).Take(180))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name) || name.Contains("unins", StringComparison.OrdinalIgnoreCase))
                continue;

            suggestions.TryAdd(path, new OpenWithAppSuggestion(name, path));
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string folder, string pattern, int maxDepth)
    {
        if (maxDepth < 0)
            yield break;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, pattern); }
        catch { yield break; }

        foreach (var file in files)
            yield return file;

        IEnumerable<string> directories;
        try { directories = Directory.EnumerateDirectories(folder); }
        catch { yield break; }

        foreach (var directory in directories)
        {
            foreach (var file in EnumerateFilesSafe(directory, pattern, maxDepth - 1))
                yield return file;
        }
    }

    private static int GetSuggestionScore(OpenWithAppSuggestion suggestion, string query)
    {
        var name = suggestion.Name;
        var path = suggestion.Path;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (path.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 2;
        return -1;
    }

    public static void SendBitmap(Bitmap bitmap, string? existingImagePath, OfficeExportTarget target)
    {
        string? tempPath = null;
        var imagePath = existingImagePath;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            tempPath = CaptureOutputService.SaveBitmapToTempPng(bitmap, "oddsnap_office");
            imagePath = tempPath;
        }

        try
        {
            SendImageFile(imagePath, target);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public static void OpenWithBitmap(Bitmap bitmap, string? existingImagePath)
    {
        string? tempPath = null;
        var imagePath = existingImagePath;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            tempPath = CaptureOutputService.SaveBitmapToTempPng(bitmap, "oddsnap_openwith");
            imagePath = tempPath;
        }

        try
        {
            ShowOpenWithDialog(imagePath);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    public static string EnsureOpenableFile(Bitmap bitmap, string? existingImagePath, out bool isTemporary)
    {
        isTemporary = false;
        if (!string.IsNullOrWhiteSpace(existingImagePath) && File.Exists(existingImagePath))
            return existingImagePath;

        isTemporary = true;
        return CaptureOutputService.SaveBitmapToTempPng(bitmap, "oddsnap_openwith");
    }

    public static void ShowOpenWithDialog(string imagePath)
    {
        try
        {
            ShowOpenWithDialogApi(imagePath);
            return;
        }
        catch
        {
            try
            {
                StartOpenAsRunDll(imagePath);
                return;
            }
            catch
            {
                StartShellOpenAs(imagePath);
            }
        }
    }

    private static void StartShellOpenAs(string imagePath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = imagePath,
            Verb = "openas",
            UseShellExecute = true
        });
    }

    private static void ShowOpenWithDialogApi(string imagePath)
    {
        var info = new OpenAsInfo
        {
            File = imagePath,
            Class = null,
            Flags = OpenAsInfoFlags.AllowRegistration | OpenAsInfoFlags.Exec
        };

        var result = SHOpenWithDialog(IntPtr.Zero, ref info);
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    private static void StartOpenAsRunDll(string imagePath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
            Arguments = $"shell32.dll,OpenAs_RunDLL {QuoteArgument(imagePath)}",
            UseShellExecute = false
        });
    }

    private static void SendImageFile(string imagePath, OfficeExportTarget target)
    {
        dynamic app = GetOrCreateApplication(target);
        app.Visible = true;

        switch (target)
        {
            case OfficeExportTarget.PowerPoint:
                SendToPowerPoint(app, imagePath);
                break;
            case OfficeExportTarget.Excel:
                SendToExcel(app, imagePath);
                break;
            default:
                SendToWord(app, imagePath);
                break;
        }
    }

    private static void SendToWord(dynamic app, string imagePath)
    {
        dynamic documents = app.Documents;
        dynamic document = documents.Count > 0 ? app.ActiveDocument : documents.Add();
        dynamic selection = app.Selection;
        selection.InlineShapes.AddPicture(imagePath, false, true);
        _ = document;
    }

    private static void SendToPowerPoint(dynamic app, string imagePath)
    {
        dynamic presentations = app.Presentations;
        dynamic presentation = presentations.Count > 0 ? app.ActivePresentation : presentations.Add();
        dynamic slide = presentation.Slides.Count > 0
            ? app.ActiveWindow.View.Slide
            : presentation.Slides.Add(1, 12);

        const float left = 36f;
        const float top = 36f;
        float maxWidth = (float)presentation.PageSetup.SlideWidth - 72f;
        float maxHeight = (float)presentation.PageSetup.SlideHeight - 72f;
        using var image = Image.FromFile(imagePath);
        var scale = Math.Min(maxWidth / image.Width, maxHeight / image.Height);
        slide.Shapes.AddPicture(imagePath, false, true, left, top, image.Width * scale, image.Height * scale);
    }

    private static void SendToExcel(dynamic app, string imagePath)
    {
        dynamic workbooks = app.Workbooks;
        dynamic workbook = workbooks.Count > 0 ? app.ActiveWorkbook : workbooks.Add();
        dynamic sheet = app.ActiveSheet;
        sheet.Pictures().Insert(imagePath);
        _ = workbook;
    }

    private static dynamic GetOrCreateApplication(OfficeExportTarget target)
    {
        var progId = GetProgId(target);
        if (TryGetActiveObject(progId, out var active) && active is not null)
            return active;

        var type = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"{GetTargetName(target)} is not installed.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Couldn't start {GetTargetName(target)}.");
    }

    private static bool TryGetActiveObject(string progId, out object? instance)
    {
        instance = null;
        if (CLSIDFromProgID(progId, out var clsid) != 0)
            return false;

        var result = GetActiveObject(ref clsid, IntPtr.Zero, out instance);
        return result == 0 && instance is not null;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo openAsInfo);

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string File;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Class;

        public OpenAsInfoFlags Flags;
    }

    [Flags]
    private enum OpenAsInfoFlags
    {
        AllowRegistration = 0x00000001,
        Exec = 0x00000004
    }
}
