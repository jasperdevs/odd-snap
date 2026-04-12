namespace Yoink.Services;

internal static class RuntimeJobFailureResolver
{
    public static string? GetFailureMessage(params BackgroundRuntimeJobSnapshot?[] snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot is { LastSucceeded: false } && !string.IsNullOrWhiteSpace(snapshot.LastError))
                return snapshot.LastError;
        }

        return null;
    }
}
