using OddSnap.AppModel.Jobs;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class TypeConsolidationTests
{
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
