using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using Yoink.Models;

namespace Yoink.Services;

public sealed class HistoryEntry
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? UploadUrl { get; set; }
    public string? UploadProvider { get; set; }
}

public sealed class OcrHistoryEntry
{
    public string Text { get; set; } = "";
    public DateTime CapturedAt { get; set; }
}

public sealed class ColorHistoryEntry
{
    public string Hex { get; set; } = "";
    public DateTime CapturedAt { get; set; }
}

public sealed class HistoryService
{
    private static readonly string HistoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history");

    private static readonly string IndexPath = Path.Combine(HistoryDir, "index.json");
    private static readonly string OcrIndexPath = Path.Combine(HistoryDir, "ocr_index.json");
    private static readonly string ColorIndexPath = Path.Combine(HistoryDir, "color_index.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private List<HistoryEntry> _entries = new();
    private List<OcrHistoryEntry> _ocrEntries = new();
    private List<ColorHistoryEntry> _colorEntries = new();

    public IReadOnlyList<HistoryEntry> Entries => _entries;
    public IReadOnlyList<HistoryEntry> ImageEntries => _entries.Where(e => !IsGif(e)).ToList();
    public IReadOnlyList<HistoryEntry> GifEntries => _entries.Where(e => IsGif(e)).ToList();
    public IReadOnlyList<OcrHistoryEntry> OcrEntries => _ocrEntries;
    public IReadOnlyList<ColorHistoryEntry> ColorEntries => _colorEntries;

    private static bool IsGif(HistoryEntry e) =>
        Path.GetExtension(e.FilePath).Equals(".gif", StringComparison.OrdinalIgnoreCase);

    public void Load()
    {
        Directory.CreateDirectory(HistoryDir);

        if (File.Exists(IndexPath))
        {
            try
            {
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(
                    File.ReadAllText(IndexPath), JsonOpts) ?? new();
                _entries.RemoveAll(e => !File.Exists(e.FilePath));
            }
            catch { _entries = new(); }
        }

        if (File.Exists(OcrIndexPath))
        {
            try
            {
                _ocrEntries = JsonSerializer.Deserialize<List<OcrHistoryEntry>>(
                    File.ReadAllText(OcrIndexPath), JsonOpts) ?? new();
            }
            catch { _ocrEntries = new(); }
        }

        if (File.Exists(ColorIndexPath))
        {
            try
            {
                _colorEntries = JsonSerializer.Deserialize<List<ColorHistoryEntry>>(
                    File.ReadAllText(ColorIndexPath), JsonOpts) ?? new();
            }
            catch { _colorEntries = new(); }
        }

        PruneByRetention(HistoryRetentionPeriod.Never);
    }

    /// <summary>
    /// Scans one or more directories for image files not tracked in the index
    /// and adds them so the history is complete. Call after Load().
    /// </summary>
    public void RecoverFromDirectories(params string[] dirs)
    {
        var tracked = new HashSet<string>(_entries.Select(e => e.FilePath), StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")) continue;
                if (tracked.Contains(file)) continue;

                try
                {
                    var fi = new FileInfo(file);
                    _entries.Add(new HistoryEntry
                    {
                        FileName = fi.Name,
                        FilePath = file,
                        CapturedAt = fi.CreationTime,
                        Width = 0,
                        Height = 0
                    });
                    tracked.Add(file);
                    changed = true;
                }
                catch { }
            }
        }

        if (changed)
        {
            _entries = _entries.OrderByDescending(e => e.CapturedAt).ToList();
            SaveIndex();
        }
    }

    public bool CompressHistory { get; set; }
    public int JpegQuality { get; set; } = 85;
    public CaptureImageFormat CaptureImageFormat { get; set; } = CaptureImageFormat.Png;
    public HistoryRetentionPeriod RetentionPeriod { get; set; } = HistoryRetentionPeriod.Never;

    public HistoryEntry SaveGifEntry(string gifPath)
    {
        var fi = new FileInfo(gifPath);
        var entry = new HistoryEntry
        {
            FileName = fi.Name,
            FilePath = gifPath,
            CapturedAt = DateTime.Now,
            Width = 0,
            Height = 0
        };
        _entries.Insert(0, entry);
        SaveIndex();
        return entry;
    }

    public HistoryEntry SaveCapture(Bitmap screenshot)
    {
        Directory.CreateDirectory(HistoryDir);
        var now = DateTime.Now;
        string ext = CaptureOutputService.GetExtension(CaptureImageFormat);
        var fileName = $"yoink_{now:yyyyMMdd_HHmmss_fff}.{ext}";
        var filePath = Path.Combine(HistoryDir, fileName);

        CaptureOutputService.SaveBitmap(screenshot, filePath, CaptureImageFormat, JpegQuality);

        var entry = new HistoryEntry
        {
            FileName = fileName, FilePath = filePath, CapturedAt = now,
            Width = screenshot.Width, Height = screenshot.Height
        };
        _entries.Insert(0, entry);
        SaveIndex();
        return entry;
    }

    public void SaveOcrEntry(string text)
    {
        _ocrEntries.Insert(0, new OcrHistoryEntry { Text = text, CapturedAt = DateTime.Now });
        while (_ocrEntries.Count > 200)
            _ocrEntries.RemoveAt(_ocrEntries.Count - 1);
        SaveOcrIndex();
    }

    public void DeleteEntry(HistoryEntry entry)
    {
        _entries.Remove(entry);
        try { File.Delete(entry.FilePath); } catch { }
        SaveIndex();
    }

    public void DeleteOcrEntry(OcrHistoryEntry entry)
    {
        _ocrEntries.Remove(entry);
        SaveOcrIndex();
    }

    public void SaveColorEntry(string hex)
    {
        _colorEntries.Insert(0, new ColorHistoryEntry { Hex = hex, CapturedAt = DateTime.Now });
        while (_colorEntries.Count > 200)
            _colorEntries.RemoveAt(_colorEntries.Count - 1);
        SaveColorIndex();
    }

    public void DeleteColorEntry(ColorHistoryEntry entry)
    {
        _colorEntries.Remove(entry);
        SaveColorIndex();
    }

    public void ClearImages()
    {
        var images = _entries.Where(e => !IsGif(e)).ToList();
        foreach (var e in images)
        {
            try { File.Delete(e.FilePath); } catch { }
            _entries.Remove(e);
        }
        SaveIndex();
    }

    public void ClearGifs()
    {
        var gifs = _entries.Where(e => IsGif(e)).ToList();
        foreach (var e in gifs)
        {
            try { File.Delete(e.FilePath); } catch { }
            _entries.Remove(e);
        }
        SaveIndex();
    }

    public void ClearOcr()
    {
        _ocrEntries.Clear();
        SaveOcrIndex();
    }

    public void ClearColors()
    {
        _colorEntries.Clear();
        SaveColorIndex();
    }

    public void ClearAll()
    {
        foreach (var e in _entries)
            try { File.Delete(e.FilePath); } catch { }
        _entries.Clear();
        SaveIndex();
    }

    public void PruneByRetention(HistoryRetentionPeriod retention)
    {
        RetentionPeriod = retention;
        var cutoff = retention switch
        {
            HistoryRetentionPeriod.OneDay => DateTime.Now.AddDays(-1),
            HistoryRetentionPeriod.SevenDays => DateTime.Now.AddDays(-7),
            HistoryRetentionPeriod.ThirtyDays => DateTime.Now.AddDays(-30),
            HistoryRetentionPeriod.NinetyDays => DateTime.Now.AddDays(-90),
            _ => DateTime.MinValue
        };

        if (retention == HistoryRetentionPeriod.Never) return;

        foreach (var e in _entries.Where(e => e.CapturedAt < cutoff).ToList())
        {
            _entries.Remove(e);
            try { File.Delete(e.FilePath); } catch { }
        }
        _ocrEntries.RemoveAll(e => e.CapturedAt < cutoff);
        _colorEntries.RemoveAll(e => e.CapturedAt < cutoff);
        SaveIndex();
        SaveOcrIndex();
        SaveColorIndex();
    }

    public void SaveIndex() =>
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(_entries, JsonOpts));

    private void SaveOcrIndex() =>
        File.WriteAllText(OcrIndexPath, JsonSerializer.Serialize(_ocrEntries, JsonOpts));

    private void SaveColorIndex() =>
        File.WriteAllText(ColorIndexPath, JsonSerializer.Serialize(_colorEntries, JsonOpts));
}
