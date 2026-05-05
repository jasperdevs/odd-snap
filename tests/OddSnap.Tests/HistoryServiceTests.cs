using Xunit;
using OddSnap.Services;
using System.Reflection;

namespace OddSnap.Tests;

public sealed class HistoryServiceTests
{
    [Fact]
    public void HistoryStorageLivesInPictures()
    {
        var picturesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "OddSnap History");

        Assert.Equal(picturesRoot, HistoryService.HistoryDir);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), HistoryService.HistoryDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), HistoryService.HistoryDir, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(picturesRoot, "history.db"), HistoryService.DatabasePath);
    }

    [Fact]
    public void NotifyChanged_ContinuesWhenOneHandlerThrows()
    {
        var service = new HistoryService();
        bool healthyHandlerCalled = false;
        service.Changed += () => throw new InvalidOperationException("boom");
        service.Changed += () => healthyHandlerCalled = true;

        var notifyChanged = typeof(HistoryService).GetMethod("NotifyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(notifyChanged);

        var ex = Record.Exception(() => notifyChanged!.Invoke(service, null));

        Assert.Null(ex);
        Assert.True(healthyHandlerCalled);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new HistoryService();

        var ex = Record.Exception(() =>
        {
            service.Dispose();
            service.Dispose();
        });

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.webm")]
    [InlineData("clip.mkv")]
    public void GetKindForPath_RecognizesVideoFiles(string fileName)
    {
        Assert.Equal(HistoryKind.Video, HistoryEntryUtilities.GetKindForPath(fileName));
        Assert.True(HistoryEntryUtilities.IsSupportedHistoryFile(fileName));
    }

    [Fact]
    public void GetKindForPath_StillRecognizesGifAndStickerFiles()
    {
        Assert.Equal(HistoryKind.Gif, HistoryEntryUtilities.GetKindForPath("clip.gif"));
        Assert.Equal(
            HistoryKind.Sticker,
            HistoryEntryUtilities.GetKindForPath(
                Path.Combine(HistoryService.StickerDir, "sticker.png"),
                stickerDirs: [HistoryService.StickerDir]));
    }

    [Fact]
    public void HistoryFileDeleteFailuresAreLogged()
    {
        var serviceCode = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "HistoryService.cs"));
        var ioCode = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "HistoryService.IO.cs"));

        var deleteEntryBlock = GetMethodBlock(serviceCode, "public void DeleteEntry(HistoryEntry entry)");
        Assert.Contains("TryDeleteHistoryFile_NoLock(entry.FilePath, \"delete entry\");", deleteEntryBlock);

        var deleteEntriesBlock = GetMethodBlock(serviceCode, "public void DeleteEntries(IEnumerable<HistoryEntry> entries)");
        Assert.Contains("TryDeleteHistoryFile_NoLock(entry.FilePath, \"delete entries\");", deleteEntriesBlock);

        var clearAllBlock = GetMethodBlock(serviceCode, "public void ClearAll()");
        Assert.Contains("TryDeleteHistoryFile_NoLock(e.FilePath, \"clear all\");", clearAllBlock);

        var clearKindBlock = GetMethodBlock(serviceCode, "private void ClearEntriesByKind_NoLock(HistoryKind kind)");
        Assert.Contains("TryDeleteHistoryFile_NoLock(e.FilePath, $\"clear {kind}\");", clearKindBlock);

        var deleteHelperBlock = GetMethodBlock(serviceCode, "private static void TryDeleteHistoryFile_NoLock(string? filePath, string context)");
        Assert.Contains("File.Delete(filePath);", deleteHelperBlock);
        Assert.Contains("catch (Exception ex)", deleteHelperBlock);
        Assert.Contains("AppDiagnostics.LogWarning(", deleteHelperBlock);
        Assert.Contains("\"history.file-delete\"", deleteHelperBlock);

        var retentionBlock = GetMethodBlock(ioCode, "public void PruneByRetention(HistoryRetentionPeriod retention)");
        Assert.Contains("TryDeleteHistoryFile_NoLock(e.FilePath, \"retention cleanup\");", retentionBlock);

        Assert.DoesNotContain("try { File.Delete(entry.FilePath); } catch { }", serviceCode);
        Assert.DoesNotContain("try { File.Delete(e.FilePath); } catch { }", serviceCode);
        Assert.DoesNotContain("try { File.Delete(e.FilePath); } catch { }", ioCode);
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method body: {signature}");
    }

}
