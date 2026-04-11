using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using Microsoft.Win32;
using Yoink.Capture;
using Yoink.Models;
using Yoink.Services;
using Yoink.UI;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Yoink;

public partial class App
{
    private static readonly UploadDestination[] GoogleLensFallbackHosts =
    {
        UploadDestination.Catbox,
        UploadDestination.ImgBB,
        UploadDestination.Litterbox
    };

    private static string CleanErrorMessage(string? msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return "Unknown error";
        // Strip HTML responses (Imgur etc. return full HTML error pages)
        if (msg.Contains('<') && msg.Contains('>'))
        {
            // Try to extract just the title or first text content
            var titleMatch = System.Text.RegularExpressions.Regex.Match(msg, @"<title>([^<]+)</title>");
            if (titleMatch.Success) return titleMatch.Groups[1].Value.Trim();
            // Strip all tags
            var stripped = System.Text.RegularExpressions.Regex.Replace(msg, @"<[^>]+>", " ").Trim();
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ");
            if (stripped.Length > 120) stripped = stripped[..120] + "...";
            return stripped.Length > 0 ? stripped : "Server returned an error";
        }
        if (msg.Length > 150) return msg[..150] + "...";
        return msg;
    }

    private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)
    {
        Interlocked.Increment(ref _activeUploadCount);
        try
        {
            var dest = _settingsService!.Settings.ImageUploadDestination;
            var settings = _settingsService.Settings.ImageUploadSettings;
            if (UploadService.IsAiChatDestination(dest))
            {
                SoundService.PlayUploadStartSound();
                var previewBitmap = TryLoadPreviewBitmap(filePath);
                try
                {
                    var providerName = UploadService.GetAiChatProviderName(settings.AiChatProvider);
                    if (settings.AiChatProvider == AiChatProvider.GoogleLens)
                    {
                        var lensUpload = await TryUploadForGoogleLensAsync(filePath, settings);
                        if (!lensUpload.Success || string.IsNullOrWhiteSpace(lensUpload.Url))
                        {
                            var errMsg = CleanErrorMessage(lensUpload.Error);
                            var saved = Path.GetFileName(filePath);
                            ToastWindow.ShowError("Google Lens upload failed", $"Saved to {saved}\n{errMsg}", filePath);
                            return;
                        }

                        var lensUrl = UploadService.BuildGoogleLensUrl(lensUpload.Url);
                        OpenExternalUrl(lensUrl);
                        SoundService.PlayUploadDoneSound();
                        previewBitmap?.Dispose();
                        previewBitmap = null;
                        ToastWindow.Show(ToastSpec.Standard("Google Lens Ready", $"Opened from {lensUpload.ProviderName}.", filePath) with { SuppressSound = true });

                        return;
                    }

                    var startUrl = UploadService.BuildAiChatStartUrl(settings.AiChatProvider);
                    OpenExternalUrl(startUrl);
                    SoundService.PlayUploadDoneSound();
                    if (previewBitmap is not null)
                    {
                        ClipboardService.CopyToClipboard(previewBitmap, filePath);
                        ToastWindow.Show(ToastSpec.ImagePreview(
                            previewBitmap,
                            "AI Redirect Ready",
                            $"Opened {providerName}. This toast is pinned so you can drag the image in or press Ctrl+V.",
                            filePath,
                            autoPin: true,
                            transparentShell: false,
                            showOverlayButtons: true,
                            clickActionUrl: startUrl,
                            clickActionLabel: providerName) with { SuppressSound = true });
                        previewBitmap = null;
                    }
                    else
                    {
                        ToastWindow.Show(ToastSpec.Standard("AI Redirect Ready", $"Opened {providerName}. Use Ctrl+V in the chat box.", filePath) with { SuppressSound = true });
                    }
                    return;
                }
                finally
                {
                    previewBitmap?.Dispose();
                }
            }

            // Validate credentials before attempting upload
            if (!UploadService.HasCredentials(dest, settings))
            {
                var saved = filePath != null ? Path.GetFileName(filePath) : null;
                var body = saved != null ? $"Saved to {saved}\nNo API key configured" : "No API key configured";
                ToastWindow.ShowError("Upload not configured", body, filePath);
                return;
            }

            SoundService.PlayUploadStartSound();
            var result = await UploadService.UploadAsync(filePath, dest, settings);

            if (result.Success)
            {
                SoundService.PlayUploadDoneSound();

                var entry = historyEntry ?? _historyService?.Entries.FirstOrDefault(e =>
                    string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.UploadUrl = result.Url;
                    var providerName = string.IsNullOrWhiteSpace(result.ProviderName)
                        ? UploadService.GetName(dest)
                        : result.ProviderName;
                    if (string.IsNullOrWhiteSpace(entry.UploadProvider))
                    {
                        entry.UploadProvider = providerName;
                        var currentName = Path.GetFileName(entry.FilePath);
                        var prefix = providerName.ToLowerInvariant() + "_";
                        entry.FileName = currentName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            ? currentName
                            : prefix + currentName;
                    }
                    EnsureHistoryService().SaveEntry(entry);
                }

                var host = Uri.TryCreate(result.Url, UriKind.Absolute, out var uploadUri)
                    ? uploadUri.Host
                    : "link";
                ClipboardService.CopyTextToClipboard(result.Url);
                ToastWindow.Show(ToastSpec.Standard("Uploaded", $"Link copied · {host}", filePath) with { SuppressSound = true });
            }
            else
            {
                AppDiagnostics.LogWarning("upload.toast-failed", $"{UploadService.GetName(dest)} upload failed for {Path.GetFileName(filePath)}: {result.Error}");
                var errTitle = result.IsRateLimit ? "Upload rate-limited" : "Upload failed";
                var errMsg = CleanErrorMessage(result.Error);
                var saved = filePath != null ? Path.GetFileName(filePath) : null;
                var body = saved != null ? $"Saved to {saved}\n{errMsg}" : errMsg;
                ToastWindow.ShowError(errTitle, body, filePath);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("upload.toast-error", ex, $"Unexpected upload error for {Path.GetFileName(filePath)}.");
            var errMsg = CleanErrorMessage(ex.Message);
            var saved = filePath != null ? Path.GetFileName(filePath) : null;
            var body = saved != null ? $"Saved to {saved}\n{errMsg}" : errMsg;
            ToastWindow.ShowError("Upload error", body, filePath);
        }
        finally
        {
            Interlocked.Decrement(ref _activeUploadCount);
            ScheduleIdleMemoryTrim();
        }
    }

    private async Task StartAiRedirectAsync(string filePath, Services.HistoryEntry? historyEntry = null)
    {
        try
        {
            var settings = _settingsService!.Settings.ImageUploadSettings;
            SoundService.PlayUploadStartSound();
            var previewBitmap = TryLoadPreviewBitmap(filePath);
            try
            {
                var providerName = UploadService.GetAiChatProviderName(settings.AiChatProvider);
                if (settings.AiChatProvider == AiChatProvider.GoogleLens)
                {
                    var lensUpload = await TryUploadForGoogleLensAsync(filePath, settings);
                    if (!lensUpload.Success || string.IsNullOrWhiteSpace(lensUpload.Url))
                    {
                        var errMsg = CleanErrorMessage(lensUpload.Error);
                        var saved = Path.GetFileName(filePath);
                        ToastWindow.ShowError("Google Lens upload failed", $"Saved to {saved}\n{errMsg}", filePath);
                        return;
                    }

                    var lensUrl = UploadService.BuildGoogleLensUrl(lensUpload.Url);
                    OpenExternalUrl(lensUrl);
                    SoundService.PlayUploadDoneSound();
                    previewBitmap?.Dispose();
                    previewBitmap = null;
                    ToastWindow.Show(ToastSpec.Standard("Google Lens Ready", $"Opened from {lensUpload.ProviderName}.", filePath) with { SuppressSound = true });
                    return;
                }

                var startUrl = UploadService.BuildAiChatStartUrl(settings.AiChatProvider);
                OpenExternalUrl(startUrl);
                SoundService.PlayUploadDoneSound();
                if (previewBitmap is not null)
                {
                    ClipboardService.CopyToClipboard(previewBitmap, filePath);
                    ToastWindow.Show(ToastSpec.ImagePreview(
                        previewBitmap,
                        "AI Redirect Ready",
                        $"Opened {providerName}. This toast is pinned so you can drag the image in or press Ctrl+V.",
                        filePath,
                        autoPin: true,
                        transparentShell: false,
                        showOverlayButtons: true,
                        clickActionUrl: startUrl,
                        clickActionLabel: providerName) with { SuppressSound = true });
                    previewBitmap = null;
                }
                else
                {
                    ToastWindow.Show(ToastSpec.Standard("AI Redirect Ready", $"Opened {providerName}. Use Ctrl+V in the chat box.", filePath) with { SuppressSound = true });
                }
            }
            finally
            {
                previewBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("ai-redirect.error", ex, $"Unexpected AI redirect error for {Path.GetFileName(filePath)}.");
            ToastWindow.ShowError("AI Redirect failed", CleanErrorMessage(ex.Message), filePath);
        }
        finally
        {
            ScheduleIdleMemoryTrim();
        }
    }

    private static void OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("No browser URL was generated for the upload.");

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private string? ResolveSavePath(string defaultPath, CaptureImageFormat format)
    {
        if (!_settingsService!.Settings.AskForFileNameOnSave)
            return defaultPath;

        var dlg = new SaveFileDialog
        {
            InitialDirectory = Path.GetDirectoryName(defaultPath),
            FileName = Path.GetFileName(defaultPath),
            Filter = format switch
            {
                CaptureImageFormat.Jpeg => "JPEG image|*.jpg;*.jpeg|PNG image|*.png|Bitmap image|*.bmp",
                CaptureImageFormat.Bmp => "Bitmap image|*.bmp|PNG image|*.png|JPEG image|*.jpg;*.jpeg",
                _ => "PNG image|*.png|JPEG image|*.jpg;*.jpeg|Bitmap image|*.bmp"
            },
            AddExtension = true,
            OverwritePrompt = true
        };

        RegionOverlayForm.CloseTransientUi();
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.IsActive)
            ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
        return owner is null
            ? (dlg.ShowDialog() == true ? dlg.FileName : null)
            : (dlg.ShowDialog(owner) == true ? dlg.FileName : null);
    }

    private static Bitmap? TryLoadPreviewBitmap(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var source = new Bitmap(stream);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GoogleLensUploadAttempt> TryUploadForGoogleLensAsync(string filePath, UploadSettings settings)
    {
        var primary = UploadService.NormalizeAiChatUploadDestination(settings.AiChatUploadDestination);
        var candidates = new List<UploadDestination> { primary };
        foreach (var fallback in GoogleLensFallbackHosts)
        {
            if (!candidates.Contains(fallback))
                candidates.Add(fallback);
        }

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!UploadService.HasCredentials(candidate, settings))
            {
                errors.Add($"{UploadService.GetName(candidate)} not configured");
                continue;
            }

            var result = await UploadService.UploadAsync(filePath, candidate, settings);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Url))
            {
                return new GoogleLensUploadAttempt
                {
                    Success = true,
                    Url = result.Url,
                    Destination = candidate,
                    ProviderName = string.IsNullOrWhiteSpace(result.ProviderName) ? UploadService.GetName(candidate) : result.ProviderName
                };
            }

            errors.Add($"{UploadService.GetName(candidate)}: {CleanErrorMessage(result.Error)}");
        }

        return new GoogleLensUploadAttempt
        {
            Error = string.Join(" | ", errors.Where(error => !string.IsNullOrWhiteSpace(error))),
            Destination = primary
        };
    }

    private sealed class GoogleLensUploadAttempt
    {
        public bool Success { get; init; }
        public string Url { get; init; } = "";
        public string Error { get; init; } = "";
        public UploadDestination Destination { get; init; }
        public string ProviderName { get; init; } = "";
    }
}
