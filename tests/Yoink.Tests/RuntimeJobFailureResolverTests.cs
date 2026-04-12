using Xunit;
using Yoink.Services;

namespace Yoink.Tests;

public sealed class RuntimeJobFailureResolverTests
{
    [Fact]
    public void GetFailureMessage_PrefersFirstFailedSnapshotWithError()
    {
        var modelFailure = new BackgroundRuntimeJobSnapshot("model", "Model", false, "Failed", false, "Model failed");
        var runtimeFailure = new BackgroundRuntimeJobSnapshot("runtime", "Runtime", false, "Failed", false, "Runtime failed");

        var result = RuntimeJobFailureResolver.GetFailureMessage(modelFailure, runtimeFailure);

        Assert.Equal("Model failed", result);
    }

    [Fact]
    public void GetFailureMessage_FallsBackToLaterFailedSnapshot()
    {
        var healthy = new BackgroundRuntimeJobSnapshot("model", "Model", false, "Ready", true, null);
        var runtimeFailure = new BackgroundRuntimeJobSnapshot("runtime", "Runtime", false, "Failed", false, "Runtime failed");

        var result = RuntimeJobFailureResolver.GetFailureMessage(healthy, runtimeFailure);

        Assert.Equal("Runtime failed", result);
    }

    [Fact]
    public void GetFailureMessage_IgnoresSnapshotsWithoutUsableErrors()
    {
        var incomplete = new BackgroundRuntimeJobSnapshot("model", "Model", false, "Failed", false, " ");
        var running = new BackgroundRuntimeJobSnapshot("runtime", "Runtime", true, "Running", null, null);

        var result = RuntimeJobFailureResolver.GetFailureMessage(incomplete, running);

        Assert.Null(result);
    }
}
