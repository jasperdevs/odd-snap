using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OddSnap.Services;

internal sealed record LegacyHistorySource(
    string RootDirectory,
    bool RelocateMedia,
    bool ReadDatabase = true);

internal sealed record LegacyHistoryMovedFile(string SourcePath, string DestinationPath);

internal sealed class LegacyHistoryMigrationPlan
{
    private readonly IReadOnlyList<LegacyHistoryMovedFile> _movedFiles;
    private readonly IReadOnlyList<string> _artifactsToRetire;
    private bool _completed;

    internal LegacyHistoryMigrationPlan(
        List<HistoryEntry> entries,
        List<OcrHistoryEntry> ocrEntries,
        List<ColorHistoryEntry> colorEntries,
        List<CodeHistoryEntry> codeEntries,
        IReadOnlyList<LegacyHistoryMovedFile> movedFiles,
        IReadOnlyList<string> artifactsToRetire)
    {
        Entries = entries;
        OcrEntries = ocrEntries;
        ColorEntries = colorEntries;
        CodeEntries = codeEntries;
        _movedFiles = movedFiles;
        _artifactsToRetire = artifactsToRetire;
    }

    public List<HistoryEntry> Entries { get; }
    public List<OcrHistoryEntry> OcrEntries { get; }
    public List<ColorHistoryEntry> ColorEntries { get; }
    public List<CodeHistoryEntry> CodeEntries { get; }
    public bool RequiresDestinationCommit => _movedFiles.Count > 0 || _artifactsToRetire.Count > 0;

    public void Commit()
    {
        if (_completed)
            return;

        SqliteConnection.ClearAllPools();
        foreach (var path in _artifactsToRetire.Distinct(StringComparer.OrdinalIgnoreCase))
            RetireArtifact(path);

        _completed = true;
    }

    public void Rollback()
    {
        if (_completed)
            return;

        foreach (var movedFile in _movedFiles.Reverse())
        {
            try
            {
                if (!File.Exists(movedFile.DestinationPath) || File.Exists(movedFile.SourcePath))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(movedFile.SourcePath) ?? AppContext.BaseDirectory);
                File.Move(movedFile.DestinationPath, movedFile.SourcePath);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError(
                    "history.migrate.rollback",
                    ex,
                    $"Failed to restore legacy history file {movedFile.SourcePath}.");
            }
        }

        _completed = true;
    }

    private static void RetireArtifact(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, path + ".migrated", overwrite: true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "history.migrate.retire-artifact",
                $"The committed legacy history artifact could not be retired: {path}.",
                ex);
        }
    }
}

internal static class LegacyHistoryMigrator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LegacyHistoryMigrationPlan Prepare(
        IReadOnlyList<HistoryEntry> currentEntries,
        IReadOnlyList<OcrHistoryEntry> currentOcrEntries,
        IReadOnlyList<ColorHistoryEntry> currentColorEntries,
        IReadOnlyList<CodeHistoryEntry> currentCodeEntries,
        string destinationHistoryDirectory,
        string destinationStickerDirectory,
        IReadOnlyList<LegacyHistorySource> sources)
    {
        var movedFiles = new List<LegacyHistoryMovedFile>();
        var artifactsToRetire = new List<string>();

        try
        {
            var normalizedSources = sources
                .Where(source => !string.IsNullOrWhiteSpace(source.RootDirectory))
                .Select(source => source with { RootDirectory = Path.GetFullPath(source.RootDirectory) })
                .DistinctBy(
                    source => $"{source.RootDirectory}|{source.RelocateMedia}|{source.ReadDatabase}",
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var relocatingSources = normalizedSources.Where(source => source.RelocateMedia).ToArray();
            var importedEntries = new Dictionary<string, HistoryEntry>(StringComparer.OrdinalIgnoreCase);
            var importedOcrEntries = new List<OcrHistoryEntry>();
            var importedColorEntries = new List<ColorHistoryEntry>();
            var importedCodeEntries = new List<CodeHistoryEntry>();

            foreach (var source in normalizedSources)
            {
                ImportSourceMetadata(
                    source,
                    importedEntries,
                    importedOcrEntries,
                    importedColorEntries,
                    importedCodeEntries,
                    artifactsToRetire);
            }

            // Entries already loaded from the destination database take precedence. This
            // also repairs databases written by the old load-before-migrate sequence.
            foreach (var entry in currentEntries)
            {
                if (FindContainingSource(entry.FilePath, relocatingSources) is not null)
                    importedEntries[Path.GetFullPath(entry.FilePath)] = HistoryEntryUtilities.CloneEntry(entry);
            }

            var entries = currentEntries
                .Where(entry => FindContainingSource(entry.FilePath, relocatingSources) is null)
                .Select(HistoryEntryUtilities.CloneEntry)
                .ToList();
            var entriesByPath = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath))
                .ToDictionary(entry => Path.GetFullPath(entry.FilePath), StringComparer.OrdinalIgnoreCase);
            var processedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (sourcePath, metadata) in importedEntries.OrderBy(pair => pair.Value.CapturedAt))
            {
                if (!File.Exists(sourcePath))
                    continue;

                var containingSource = FindContainingSource(sourcePath, relocatingSources);
                var destinationPath = containingSource is null
                    ? Path.GetFullPath(sourcePath)
                    : MoveLegacyMedia(
                        sourcePath,
                        metadata.Kind,
                        containingSource,
                        destinationHistoryDirectory,
                        destinationStickerDirectory,
                        movedFiles);

                processedSourcePaths.Add(Path.GetFullPath(sourcePath));
                var migratedEntry = BuildMigratedEntry(destinationPath, metadata, destinationStickerDirectory);
                if (!entriesByPath.ContainsKey(migratedEntry.FilePath))
                {
                    entries.Add(migratedEntry);
                    entriesByPath[migratedEntry.FilePath] = migratedEntry;
                }
            }

            foreach (var source in relocatingSources)
            {
                if (!Directory.Exists(source.RootDirectory))
                    continue;

                var mediaPaths = Directory
                    .EnumerateFiles(source.RootDirectory, "*", SearchOption.AllDirectories)
                    .Where(path => IsLegacyMediaCandidate(path, source.RootDirectory))
                    .ToArray();

                foreach (var sourcePath in mediaPaths)
                {
                    var normalizedSourcePath = Path.GetFullPath(sourcePath);
                    if (processedSourcePaths.Contains(normalizedSourcePath) || !File.Exists(normalizedSourcePath))
                        continue;

                    var sourceStickerDirectory = Path.Combine(source.RootDirectory, "stickers");
                    var kind = HistoryEntryUtilities.GetKindForPath(
                        normalizedSourcePath,
                        stickerDirs: [sourceStickerDirectory]);
                    var destinationPath = MoveLegacyMedia(
                        normalizedSourcePath,
                        kind,
                        source,
                        destinationHistoryDirectory,
                        destinationStickerDirectory,
                        movedFiles);
                    var migratedEntry = BuildMigratedEntry(destinationPath, metadata: null, destinationStickerDirectory);
                    if (!entriesByPath.ContainsKey(migratedEntry.FilePath))
                    {
                        entries.Add(migratedEntry);
                        entriesByPath[migratedEntry.FilePath] = migratedEntry;
                    }
                }
            }

            return new LegacyHistoryMigrationPlan(
                entries.OrderByDescending(entry => entry.CapturedAt).ToList(),
                MergeOcrEntries(currentOcrEntries, importedOcrEntries),
                MergeColorEntries(currentColorEntries, importedColorEntries),
                MergeCodeEntries(currentCodeEntries, importedCodeEntries),
                movedFiles,
                artifactsToRetire);
        }
        catch
        {
            new LegacyHistoryMigrationPlan([], [], [], [], movedFiles, artifactsToRetire).Rollback();
            throw;
        }
    }

    private static void ImportSourceMetadata(
        LegacyHistorySource source,
        Dictionary<string, HistoryEntry> importedEntries,
        List<OcrHistoryEntry> importedOcrEntries,
        List<ColorHistoryEntry> importedColorEntries,
        List<CodeHistoryEntry> importedCodeEntries,
        List<string> artifactsToRetire)
    {
        if (!Directory.Exists(source.RootDirectory))
            return;

        var databasePath = Path.Combine(source.RootDirectory, "history.db");
        if (source.ReadDatabase && File.Exists(databasePath))
        {
            try
            {
                HistoryStore.EnsureDatabase(databasePath);
                var database = HistoryStore.Load(databasePath);
                foreach (var entry in database.Entries)
                    AddImportedEntry(importedEntries, source, entry, overwrite: true);
                importedOcrEntries.AddRange(database.OcrEntries);
                importedColorEntries.AddRange(database.ColorEntries);
                importedCodeEntries.AddRange(database.CodeEntries);
                artifactsToRetire.Add(databasePath);
                AddIfExists(artifactsToRetire, databasePath + "-wal");
                AddIfExists(artifactsToRetire, databasePath + "-shm");
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "history.migrate.database",
                    $"Failed to read legacy history database {databasePath}.",
                    ex);
            }
        }

        ImportJson<HistoryEntry>(
            Path.Combine(source.RootDirectory, "index.json"),
            entries =>
            {
                foreach (var entry in entries)
                    AddImportedEntry(importedEntries, source, entry, overwrite: false);
            },
            artifactsToRetire);
        ImportJson<OcrHistoryEntry>(
            Path.Combine(source.RootDirectory, "ocr_index.json"),
            entries => importedOcrEntries.AddRange(entries),
            artifactsToRetire);
        ImportJson<ColorHistoryEntry>(
            Path.Combine(source.RootDirectory, "color_index.json"),
            entries => importedColorEntries.AddRange(entries),
            artifactsToRetire);
        ImportJson<CodeHistoryEntry>(
            Path.Combine(source.RootDirectory, "code_index.json"),
            entries => importedCodeEntries.AddRange(entries),
            artifactsToRetire);
    }

    private static void AddImportedEntry(
        IDictionary<string, HistoryEntry> importedEntries,
        LegacyHistorySource source,
        HistoryEntry entry,
        bool overwrite)
    {
        var sourcePath = ResolveEntryPath(source.RootDirectory, entry);
        if (sourcePath is null)
            return;

        var clone = HistoryEntryUtilities.CloneEntry(entry);
        clone.FilePath = sourcePath;
        clone.FileName = Path.GetFileName(sourcePath);
        if (overwrite || !importedEntries.ContainsKey(sourcePath))
            importedEntries[sourcePath] = clone;
    }

    private static string? ResolveEntryPath(string sourceRoot, HistoryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
            return Path.GetFullPath(entry.FilePath);

        if (string.IsNullOrWhiteSpace(entry.FileName))
            return null;

        var candidateDirectory = entry.Kind == HistoryKind.Sticker
            ? Path.Combine(sourceRoot, "stickers")
            : sourceRoot;
        var candidate = Path.Combine(candidateDirectory, Path.GetFileName(entry.FileName));
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private static void ImportJson<T>(
        string path,
        Action<List<T>> import,
        ICollection<string> artifactsToRetire)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? [];
            import(entries);
            artifactsToRetire.Add(path);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "history.migrate.json",
                $"Failed to read legacy history index {path}.",
                ex);
        }
    }

    private static LegacyHistorySource? FindContainingSource(
        string path,
        IReadOnlyList<LegacyHistorySource> sources)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return sources.FirstOrDefault(source =>
            HistoryEntryUtilities.IsPathWithinDirectory(path, source.RootDirectory));
    }

    private static string MoveLegacyMedia(
        string sourcePath,
        HistoryKind kind,
        LegacyHistorySource source,
        string destinationHistoryDirectory,
        string destinationStickerDirectory,
        ICollection<LegacyHistoryMovedFile> movedFiles)
    {
        var sourceStickerDirectory = Path.Combine(source.RootDirectory, "stickers");
        var destinationDirectory =
            kind == HistoryKind.Sticker ||
            HistoryEntryUtilities.IsPathWithinDirectory(sourcePath, sourceStickerDirectory)
                ? destinationStickerDirectory
                : destinationHistoryDirectory;
        Directory.CreateDirectory(destinationDirectory);

        var requestedPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        var destinationPath = HistoryMigrationPathResolver.ResolveAvailablePath(requestedPath);
        File.Move(sourcePath, destinationPath);
        movedFiles.Add(new LegacyHistoryMovedFile(sourcePath, destinationPath));
        return Path.GetFullPath(destinationPath);
    }

    private static HistoryEntry BuildMigratedEntry(
        string destinationPath,
        HistoryEntry? metadata,
        string destinationStickerDirectory)
    {
        var file = new FileInfo(destinationPath);
        var fallbackKind = metadata is not null && Enum.IsDefined(metadata.Kind)
            ? metadata.Kind
            : HistoryKind.Image;

        return new HistoryEntry
        {
            FileName = file.Name,
            FilePath = file.FullName,
            CapturedAt = metadata?.CapturedAt ?? file.CreationTime,
            Width = metadata?.Width ?? 0,
            Height = metadata?.Height ?? 0,
            FileSizeBytes = file.Length,
            Kind = HistoryEntryUtilities.GetKindForPath(
                file.FullName,
                fallbackKind,
                destinationStickerDirectory),
            UploadUrl = metadata?.UploadUrl,
            UploadProvider = metadata?.UploadProvider,
            UploadError = metadata?.UploadError
        };
    }

    private static bool IsLegacyMediaCandidate(string path, string sourceRoot)
    {
        if (!HistoryEntryUtilities.IsSupportedHistoryFile(path))
            return false;

        var relativePath = Path.GetRelativePath(sourceRoot, path);
        return !relativePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals(".thumbs", StringComparison.OrdinalIgnoreCase));
    }

    private static List<OcrHistoryEntry> MergeOcrEntries(
        IReadOnlyList<OcrHistoryEntry> current,
        IEnumerable<OcrHistoryEntry> imported) =>
        current
            .Concat(imported)
            .DistinctBy(entry => (entry.Text, entry.CapturedAt))
            .OrderByDescending(entry => entry.CapturedAt)
            .ToList();

    private static List<ColorHistoryEntry> MergeColorEntries(
        IReadOnlyList<ColorHistoryEntry> current,
        IEnumerable<ColorHistoryEntry> imported) =>
        current
            .Concat(imported)
            .DistinctBy(entry => (entry.Hex, entry.CapturedAt))
            .OrderByDescending(entry => entry.CapturedAt)
            .ToList();

    private static List<CodeHistoryEntry> MergeCodeEntries(
        IReadOnlyList<CodeHistoryEntry> current,
        IEnumerable<CodeHistoryEntry> imported) =>
        current
            .Concat(imported)
            .DistinctBy(entry => (entry.Text, entry.Format, entry.CapturedAt))
            .OrderByDescending(entry => entry.CapturedAt)
            .ToList();

    private static void AddIfExists(ICollection<string> paths, string path)
    {
        if (File.Exists(path))
            paths.Add(path);
    }
}
