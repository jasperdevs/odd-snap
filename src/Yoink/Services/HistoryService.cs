using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace Yoink.Services;

public sealed class HistoryEntry
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
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
    public IReadOnlyList<OcrHistoryEntry> OcrEntries => _ocrEntries;
    public IReadOnlyList<ColorHistoryEntry> ColorEntries => _colorEntries;

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
    }

    public bool CompressHistory { get; set; }
    public int JpegQuality { get; set; } = 85;

    public HistoryEntry SaveCapture(Bitmap screenshot)
    {
        Directory.CreateDirectory(HistoryDir);
        var now = DateTime.Now;
        string ext = CompressHistory ? "jpg" : "png";
        var fileName = $"yoink_{now:yyyyMMdd_HHmmss_fff}.{ext}";
        var filePath = Path.Combine(HistoryDir, fileName);

        if (CompressHistory)
        {
            var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
            var param = new EncoderParameters(1);
            param.Param[0] = new EncoderParameter(Encoder.Quality, (long)JpegQuality);
            screenshot.Save(filePath, encoder, param);
        }
        else
        {
            screenshot.Save(filePath, ImageFormat.Png);
        }

        var entry = new HistoryEntry
        {
            FileName = fileName, FilePath = filePath, CapturedAt = now,
            Width = screenshot.Width, Height = screenshot.Height
        };
        _entries.Insert(0, entry);

        while (_entries.Count > 200)
        {
            var old = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            try { File.Delete(old.FilePath); } catch { }
        }
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
        foreach (var e in _entries)
            try { File.Delete(e.FilePath); } catch { }
        _entries.Clear();
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

    private void SaveIndex() =>
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(_entries, JsonOpts));

    private void SaveOcrIndex() =>
        File.WriteAllText(OcrIndexPath, JsonSerializer.Serialize(_ocrEntries, JsonOpts));

    private void SaveColorIndex() =>
        File.WriteAllText(ColorIndexPath, JsonSerializer.Serialize(_colorEntries, JsonOpts));
}
