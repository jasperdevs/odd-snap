using Xunit;

namespace OddSnap.Tests;

public sealed class BackgroundRuntimeJobServiceTests
{
    [Fact]
    public void PersistTempCleanupFailuresAreLogged()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var persistBlock = GetMethodBlock(source, "private static void Persist_NoLock()");
        var cleanupBlock = GetMethodBlock(source, "private static void TryDeletePersistTempFile(string tempPath)");

        Assert.Contains("var tempPath = PersistPath + \".tmp\";", persistBlock);
        Assert.Contains("File.WriteAllText(tempPath", persistBlock);
        Assert.Contains("File.Move(tempPath, PersistPath, overwrite: true);", persistBlock);
        Assert.Contains("_persistFailureToastShown = false;", persistBlock);
        Assert.Contains("TryDeletePersistTempFile(tempPath);", persistBlock);
        Assert.Contains("DispatchRuntimeJobPersistenceWarningToast();", persistBlock);
        Assert.Contains("\"runtime-jobs.temp-cleanup\"", cleanupBlock);
        Assert.Contains("AppDiagnostics.LogWarning(", cleanupBlock);
        Assert.Contains("Path.GetFileName(tempPath)", cleanupBlock);
    }

    [Fact]
    public void RuntimeJobFailureToastIncludesRecoveryCopy()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var completeBlock = GetMethodBlock(source, "private static void Complete(BackgroundRuntimeJobOptions options, bool success, Exception? error)");
        var dispatchBlock = GetMethodBlock(source, "private static void DispatchToast(BackgroundRuntimeJobOptions options, bool success, string? errorMessage)");
        var bodyBlock = GetMethodBlock(source, "private static string BuildRuntimeJobFailureToastBody(BackgroundRuntimeJobOptions options, string? errorMessage)");
        var toastDetailBlock = GetMethodBlock(source, "private static string BuildRuntimeJobToastDetail(string? errorMessage)");
        var normalizeBlock = GetMethodBlock(source, "private static string NormalizeRuntimeJobText(string? text, string fallback)");

        Assert.Contains(": \"Failed. Retry from Settings.\";", completeBlock);
        Assert.DoesNotContain(": $\"Failed: {errorMessage}\";", completeBlock);
        Assert.Contains("ToastWindow.ShowError(options.FailureTitle, BuildRuntimeJobFailureToastBody(options, errorMessage));", dispatchBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(options.FailureTitle, string.IsNullOrWhiteSpace(errorMessage) ? \"Unknown error.\" : errorMessage);", dispatchBlock);
        Assert.Contains("Close anything using {options.Label}, then retry from Settings.", bodyBlock);
        Assert.Contains("Check Settings and retry {options.Label}.", bodyBlock);
        Assert.Contains("var details = BuildRuntimeJobToastDetail(errorMessage);", bodyBlock);
        Assert.Contains("return $\"{recovery}\\n{details}\";", bodyBlock);
        Assert.Contains("private const int MaxRuntimeJobToastDetailLength = 260;", source);
        Assert.Contains("var detail = NormalizeRuntimeJobText(errorMessage, \"Unknown error.\");", toastDetailBlock);
        Assert.Contains("Details were shortened; check Settings or logs for the full output.", toastDetailBlock);
        Assert.Contains("source.Split([' ', '\\t', '\\r', '\\n'], StringSplitOptions.RemoveEmptyEntries)", normalizeBlock);
    }

    [Fact]
    public void RuntimeJobProgressStatusIsSingleLineAndBounded()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var startBlock = GetMethodBlock(source, "public static bool Start(");
        var progressStatusBlock = GetMethodBlock(source, "private static string BuildRuntimeJobProgressStatus(string? message, string fallbackStatus)");

        Assert.Contains("private const int MaxRuntimeJobStatusLength = 120;", source);
        Assert.Contains("UpdateStatus(options.Key, BuildRuntimeJobProgressStatus(message, options.StartingStatus));", startBlock);
        Assert.Contains("var fallback = string.IsNullOrWhiteSpace(fallbackStatus) ? \"Working...\" : fallbackStatus;", progressStatusBlock);
        Assert.Contains("var status = NormalizeRuntimeJobText(message, fallback);", progressStatusBlock);
        Assert.Contains("status[..(MaxRuntimeJobStatusLength - 3)].TrimEnd() + \"...\"", progressStatusBlock);
    }

    [Fact]
    public void InterruptedRuntimeJobsPersistRecoveryStatus()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var normalizeBlock = GetMethodBlock(source, "private static void NormalizeInterruptedJobs_NoLock()");
        var messageBlock = GetMethodBlock(source, "private static string BuildInterruptedRuntimeJobMessage(string label)");

        Assert.Contains("state.LastError = BuildInterruptedRuntimeJobMessage(state.Label);", normalizeBlock);
        Assert.Contains("state.Status = \"Interrupted. Retry from Settings.\";", normalizeBlock);
        Assert.DoesNotContain("state.LastError = \"Interrupted because OddSnap closed before setup finished.\";", normalizeBlock);
        Assert.DoesNotContain("state.Status = \"Interrupted - retry setup\";", normalizeBlock);
        Assert.Contains("var jobLabel = string.IsNullOrWhiteSpace(label) ? \"runtime setup\" : label;", messageBlock);
        Assert.Contains("Retry from Settings.", messageBlock);
    }

    [Fact]
    public void CorruptRuntimeJobStateIsQuarantinedAfterLoadFailure()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var loadBlock = GetMethodBlock(source, "private static void LoadPersisted_NoLock()");
        var quarantineBlock = GetMethodBlock(source, "private static void TryQuarantinePersistedJobsFile()");

        Assert.Contains("AppDiagnostics.LogError(\"runtime-jobs.load\", ex);", loadBlock);
        Assert.Contains("Jobs.Clear();", loadBlock);
        Assert.Contains("TryQuarantinePersistedJobsFile();", loadBlock);
        Assert.Contains("if (!File.Exists(PersistPath))", quarantineBlock);
        Assert.Contains("var quarantinePath = PersistPath + \".bad\";", quarantineBlock);
        Assert.Contains("File.Move(PersistPath, quarantinePath, overwrite: true);", quarantineBlock);
        Assert.Contains("\"runtime-jobs.quarantine\"", quarantineBlock);
        Assert.Contains("Path.GetFileName(quarantinePath)", quarantineBlock);
        Assert.Contains("Path.GetFileName(PersistPath)", quarantineBlock);
    }

    [Fact]
    public void PersistFailuresShowOneRecoveryToast()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var warningBlock = GetMethodBlock(source, "private static void DispatchRuntimeJobPersistenceWarningToast()");

        Assert.Contains("private static bool _persistFailureToastShown;", source);
        Assert.Contains("if (_persistFailureToastShown)", warningBlock);
        Assert.Contains("_persistFailureToastShown = true;", warningBlock);
        Assert.Contains("ToastWindow.ShowError(", warningBlock);
        Assert.Contains("\"Runtime status not saved\"", warningBlock);
        Assert.Contains("Runtime setup can continue, but its status may not survive restart.", warningBlock);
        Assert.Contains("Check Settings and retry if needed.", warningBlock);
    }

    [Fact]
    public void CancelledRuntimeJobsPersistRecoveryStatusWithoutFailureToast()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "BackgroundRuntimeJobService.cs"));
        var completeCancelledBlock = GetMethodBlock(source, "private static void CompleteCancelled(BackgroundRuntimeJobOptions options)");
        var messageBlock = GetMethodBlock(source, "private static string BuildCancelledRuntimeJobMessage(string label)");

        Assert.Contains("CompleteCancelled(options);", source);
        Assert.DoesNotContain("Complete(options, success: false, error: new OperationCanceledException(\"Cancelled.\"));", source);
        Assert.Contains("state.LastError = message;", completeCancelledBlock);
        Assert.Contains("state.Status = \"Cancelled. Retry from Settings.\";", completeCancelledBlock);
        Assert.Contains("AppDiagnostics.LogInfo(\"runtime-jobs.cancelled\"", completeCancelledBlock);
        Assert.DoesNotContain("DispatchToast(", completeCancelledBlock);
        Assert.Contains("var jobLabel = string.IsNullOrWhiteSpace(label) ? \"runtime setup\" : label;", messageBlock);
        Assert.Contains("was cancelled before it finished. Retry from Settings.", messageBlock);
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
