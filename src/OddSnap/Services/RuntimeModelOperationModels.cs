namespace OddSnap.Services;

public sealed record RuntimeModelDownloadProgress(long BytesReceived, long? TotalBytes, string StatusMessage)
{
    public double Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : 0d;
}

public sealed record RuntimeModelInstallResult(
    bool Success,
    string Message,
    string? ModelPath = null,
    string? ReferenceUrl = null);
