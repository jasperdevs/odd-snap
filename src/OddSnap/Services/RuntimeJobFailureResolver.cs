using OddSnap.AppModel.Jobs;

namespace OddSnap.Services;

internal static class RuntimeJobFailureResolver
{
    public static string? GetFailureMessage(params AppJobSnapshot?[] snapshots)
    {
        var snapshot = GetFirstFailedSnapshotWithError(snapshots);
        return snapshot?.LastError?.Trim();
    }

    public static string? GetFailureDiagnosticMessage(params AppJobSnapshot?[] snapshots)
    {
        var snapshot = GetFirstFailedSnapshotWithError(snapshots);
        if (snapshot is null)
            return null;

        return string.Join(
            Environment.NewLine,
            $"{snapshot.Label} failed",
            $"Status: {snapshot.Status}",
            "Details:",
            snapshot.LastError!.Trim());
    }

    private static AppJobSnapshot? GetFirstFailedSnapshotWithError(params AppJobSnapshot?[] snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot is { LastSucceeded: false } && !string.IsNullOrWhiteSpace(snapshot.LastError))
                return snapshot;
        }

        return null;
    }
}
