using OddSnap.AppModel.Jobs;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class TypeConsolidationTests
{
    [Theory]
    [InlineData(50L, 200L, 25d)]
    [InlineData(0L, 200L, 0d)]
    [InlineData(50L, null, 0d)]
    [InlineData(50L, 0L, 0d)]
    public void RuntimeModelDownloadProgress_CalculatesPercent(
        long bytesReceived,
        long? totalBytes,
        double expectedPercent)
    {
        var progress = new RuntimeModelDownloadProgress(bytesReceived, totalBytes, "Downloading...");

        Assert.Equal(expectedPercent, progress.Percent);
    }

    [Fact]
    public void RuntimeJobFailureResolver_UsesSharedAppJobSnapshot()
    {
        var snapshot = new AppJobSnapshot(
            "runtime:test",
            "Test runtime",
            AppJobArea.Runtime,
            IsRunning: false,
            Status: "Failed",
            LastSucceeded: false,
            LastError: "  setup failed  ");

        Assert.Equal("setup failed", RuntimeJobFailureResolver.GetFailureMessage(snapshot));
        Assert.Equal(
            string.Join(Environment.NewLine, "Test runtime failed", "Status: Failed", "Details:", "setup failed"),
            RuntimeJobFailureResolver.GetFailureDiagnosticMessage(snapshot));
    }
}
