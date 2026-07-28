using System.IO;
using System.Text;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.Helpers;

/// <summary>
/// Writes capture provenance ("Source app: Discord", capture time, OddSnap version) into the saved
/// image itself: PNG <c>tEXt</c> chunks, JPEG <c>COM</c> comment segments. Failures are non-fatal —
/// the capture is already on disk by the time this runs.
/// </summary>
public static class ImageMetadataWriter
{
    public const string SoftwareKey = "Software";
    public const string SourceAppKey = "Source";
    public const string CreationTimeKey = "Creation Time";

    /// <summary>Builds the standard capture metadata pairs; returns an empty list when nothing is known.</summary>
    public static List<KeyValuePair<string, string>> BuildCaptureMetadata(string? sourceApp, DateTime capturedAt)
    {
        var metadata = new List<KeyValuePair<string, string>>
        {
            new(SoftwareKey, $"OddSnap {UpdateService.GetCurrentVersionLabel()}"),
            new(CreationTimeKey, capturedAt.ToString("yyyy-MM-ddTHH:mm:ssK"))
        };

        if (!string.IsNullOrWhiteSpace(sourceApp))
            metadata.Add(new KeyValuePair<string, string>(SourceAppKey, sourceApp));

        return metadata;
    }

    public static void TryWrite(
        string filePath,
        CaptureImageFormat format,
        IReadOnlyList<KeyValuePair<string, string>> metadata)
    {
        if (metadata.Count == 0 || string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            switch (format)
            {
                case CaptureImageFormat.Png:
                    WritePngTextChunks(filePath, metadata);
                    break;
                case CaptureImageFormat.Jpeg:
                    WriteJpegComment(filePath, FormatComment(metadata));
                    break;
                // BMP has no standard place for this; the file name and history keep the attribution.
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "capture.metadata",
                $"Failed to write metadata into {Path.GetFileName(filePath)}: {ex.Message}",
                ex);
        }
    }

    private static string FormatComment(IReadOnlyList<KeyValuePair<string, string>> metadata)
        => string.Join("; ", metadata.Select(pair => $"{pair.Key}: {pair.Value}"));

    /// <summary>Inserts PNG tEXt chunks immediately before IEND.</summary>
    private static void WritePngTextChunks(string filePath, IReadOnlyList<KeyValuePair<string, string>> metadata)
    {
        var bytes = File.ReadAllBytes(filePath);
        int iendOffset = FindPngIendChunkOffset(bytes);
        if (iendOffset < 0)
            return;

        using var output = new MemoryStream(bytes.Length + 256);
        output.Write(bytes, 0, iendOffset);
        foreach (var pair in metadata)
            WritePngTextChunk(output, pair.Key, pair.Value);
        output.Write(bytes, iendOffset, bytes.Length - iendOffset);

        WriteAllBytesAtomic(filePath, output.ToArray());
    }

    private static int FindPngIendChunkOffset(byte[] bytes)
    {
        // 8-byte signature, then length(4) + type(4) + data + crc(4) chunks.
        if (bytes.Length < 12 || bytes[0] != 0x89 || bytes[1] != 'P' || bytes[2] != 'N' || bytes[3] != 'G')
            return -1;

        int offset = 8;
        while (offset + 8 <= bytes.Length)
        {
            uint length = ReadBigEndianUInt32(bytes, offset);
            if (length > int.MaxValue - 12)
                return -1;

            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type == "IEND")
                return offset;

            offset += 12 + (int)length;
        }

        return -1;
    }

    private static void WritePngTextChunk(Stream output, string keyword, string text)
    {
        // tEXt keywords are Latin-1, 1-79 characters, followed by a null separator and the value.
        var keywordBytes = Encoding.Latin1.GetBytes(keyword);
        if (keywordBytes.Length is 0 or > 79)
            return;

        var textBytes = Encoding.Latin1.GetBytes(text);
        var payload = new byte[4 + keywordBytes.Length + 1 + textBytes.Length];
        Encoding.ASCII.GetBytes("tEXt").CopyTo(payload, 0);
        keywordBytes.CopyTo(payload, 4);
        payload[4 + keywordBytes.Length] = 0;
        textBytes.CopyTo(payload, 4 + keywordBytes.Length + 1);

        WriteBigEndianUInt32(output, (uint)(payload.Length - 4));
        output.Write(payload, 0, payload.Length);
        WriteBigEndianUInt32(output, ComputeCrc32(payload));
    }

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }

        return table;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Inserts a JPEG COM segment right after the SOI marker.</summary>
    private static void WriteJpegComment(string filePath, string comment)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 2 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return;

        var commentBytes = Encoding.UTF8.GetBytes(comment);
        if (commentBytes.Length > 65_533)
            commentBytes = commentBytes[..65_533];

        int segmentLength = commentBytes.Length + 2;
        using var output = new MemoryStream(bytes.Length + segmentLength + 2);
        output.Write(bytes, 0, 2);
        output.WriteByte(0xFF);
        output.WriteByte(0xFE);
        output.WriteByte((byte)(segmentLength >> 8));
        output.WriteByte((byte)(segmentLength & 0xFF));
        output.Write(commentBytes, 0, commentBytes.Length);
        output.Write(bytes, 2, bytes.Length - 2);

        WriteAllBytesAtomic(filePath, output.ToArray());
    }

    private static void WriteAllBytesAtomic(string filePath, byte[] contents)
    {
        var tempPath = filePath + ".meta.tmp";
        try
        {
            File.WriteAllBytes(tempPath, contents);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Nothing else to do; the original capture file is untouched.
            }
            throw;
        }
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
        => (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);

    private static void WriteBigEndianUInt32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}
