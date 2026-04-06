using System.Drawing;
using System.Drawing.Imaging;
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
            // Validate credentials before attempting upload
            var dest = _settingsService!.Settings.ImageUploadDestination;
            var settings = _settingsService.Settings.ImageUploadSettings;
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
                ClipboardService.CopyTextToClipboard(result.Url);

                var entry = historyEntry ?? _historyService?.Entries.FirstOrDefault(e =>
                    string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.UploadUrl = result.Url;
                    if (string.IsNullOrWhiteSpace(entry.UploadProvider))
                    {
                        entry.UploadProvider = UploadService.GetName(dest);
                        var currentName = Path.GetFileName(entry.FilePath);
                        var prefix = UploadService.GetName(dest).ToLowerInvariant() + "_";
                        entry.FileName = currentName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            ? currentName
                            : prefix + currentName;
                    }
                    EnsureHistoryService().SaveEntry(entry);
                }

                // Show success toast with the upload URL
                var host = new Uri(result.Url).Host;
                ToastWindow.Show("Uploaded", $"Link copied · {host}", filePath);
            }
            else
            {
                var errTitle = result.IsRateLimit ? "Upload rate-limited" : "Upload failed";
                var errMsg = CleanErrorMessage(result.Error);
                var saved = filePath != null ? Path.GetFileName(filePath) : null;
                var body = saved != null ? $"Saved to {saved}\n{errMsg}" : errMsg;
                ToastWindow.ShowError(errTitle, body, filePath);
            }
        }
        catch (Exception ex)
        {
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
}
