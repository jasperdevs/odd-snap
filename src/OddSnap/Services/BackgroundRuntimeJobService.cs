using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using OddSnap.AppModel.Jobs;
using OddSnap.UI;

namespace OddSnap.Services;

public sealed record BackgroundRuntimeJobSnapshot(
    string Key,
    string Label,
    bool IsRunning,
    string Status,
    bool? LastSucceeded,
    string? LastError);

public sealed record BackgroundRuntimeJobOptions(
    string Key,
    string Label,
    string StartingStatus,
    string SuccessTitle,
    string SuccessBody,
    string FailureTitle)
{
    public string? SuccessStatus { get; init; }
    public Func<Exception, string>? FormatError { get; init; }
}

public static class BackgroundRuntimeJobService
{
    private const int MaxRuntimeJobStatusLength = 120;
    private const int MaxRuntimeJobToastDetailLength = 260;

    private sealed class JobState
    {
        public string Key { get; init; } = "";
        public string Label { get; set; } = "";
        public bool IsRunning { get; set; }
        public string Status { get; set; } = "";
        public bool? LastSucceeded { get; set; }
        public string? LastError { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
    }

    private sealed class PersistedJobState
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsRunning { get; set; }
        public string Status { get; set; } = "";
        public bool? LastSucceeded { get; set; }
        public string? LastError { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, JobState> Jobs = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string PersistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OddSnap",
        "runtime-jobs.json");
    private static bool _initialized;
    private static bool _persistFailureToastShown;

    public static event Action<string>? Changed;

    public static void Initialize()
    {
        lock (Gate)
            EnsureInitialized_NoLock();
    }

    public static bool Start(
        BackgroundRuntimeJobOptions options,
        Func<IProgress<string>, CancellationToken, Task> work)
    {
        CancellationTokenSource cancellation;
        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (Jobs.TryGetValue(options.Key, out var existing) && existing.IsRunning)
                return false;

            cancellation = new CancellationTokenSource();
            Jobs[options.Key] = new JobState
            {
                Key = options.Key,
                Label = options.Label,
                IsRunning = true,
                Status = options.StartingStatus,
                LastSucceeded = null,
                LastError = null,
                Cancellation = cancellation
            };
            Persist_NoLock();
        }

        AppDiagnostics.LogInfo("runtime-jobs.start", $"{options.Key}: {options.StartingStatus}");
        NotifyChanged(options.Key);

        _ = Task.Run(async () =>
        {
            var progress = new Progress<string>(message =>
            {
                UpdateStatus(options.Key, BuildRuntimeJobProgressStatus(message, options.StartingStatus));
            });

            try
            {
                await work(progress, cancellation.Token).ConfigureAwait(false);
                Complete(options, success: true, error: null);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                CompleteCancelled(options);
            }
            catch (Exception ex)
            {
                Complete(options, success: false, error: ex);
            }
        });

        return true;
    }

    public static bool TryGetSnapshot(string key, out BackgroundRuntimeJobSnapshot snapshot)
    {
        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (!Jobs.TryGetValue(key, out var state))
            {
                snapshot = default!;
                return false;
            }

            snapshot = ToSnapshot(state);
            return true;
        }
    }

    public static IReadOnlyList<AppJobSnapshot> GetSnapshots(AppJobArea? area = null)
    {
        lock (Gate)
        {
            EnsureInitialized_NoLock();
            var snapshots = Jobs.Values
                .Select(ToAppJobSnapshot)
                .OrderBy(snapshot => snapshot.Key, StringComparer.Ordinal)
                .ToList();

            return area is null
                ? snapshots
                : snapshots.Where(snapshot => snapshot.Area == area.Value).ToList();
        }
    }

    public static bool TryGetAppJobSnapshot(string key, out AppJobSnapshot snapshot)
    {
        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (!Jobs.TryGetValue(key, out var state))
            {
                snapshot = default!;
                return false;
            }

            snapshot = ToAppJobSnapshot(state);
            return true;
        }
    }

    private static void UpdateStatus(string key, string status)
    {
        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (!Jobs.TryGetValue(key, out var state))
                return;

            state.Status = status;
            Persist_NoLock();
        }

        NotifyChanged(key);
    }

    private static void Complete(BackgroundRuntimeJobOptions options, bool success, Exception? error)
    {
        string? errorMessage = null;

        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (!Jobs.TryGetValue(options.Key, out var state))
                return;

            errorMessage = error is null
                ? null
                : (options.FormatError?.Invoke(error) ?? error.Message);

            state.IsRunning = false;
            state.LastSucceeded = success;
            state.LastError = errorMessage;
            state.Status = success
                ? (options.SuccessStatus ?? "Ready")
                : "Failed. Retry from Settings.";
            state.Cancellation?.Dispose();
            state.Cancellation = null;
            Persist_NoLock();
        }

        NotifyChanged(options.Key);
        if (success)
            AppDiagnostics.LogInfo("runtime-jobs.complete", $"{options.Key}: {options.SuccessStatus ?? "Ready"}");
        else
            AppDiagnostics.LogWarning("runtime-jobs.complete", $"{options.Key}: {errorMessage ?? "Unknown error"}");
        DispatchToast(options, success, errorMessage);
    }

    private static void CompleteCancelled(BackgroundRuntimeJobOptions options)
    {
        var message = BuildCancelledRuntimeJobMessage(options.Label);

        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (!Jobs.TryGetValue(options.Key, out var state))
                return;

            state.IsRunning = false;
            state.LastSucceeded = false;
            state.LastError = message;
            state.Status = "Cancelled. Retry from Settings.";
            state.Cancellation?.Dispose();
            state.Cancellation = null;
            Persist_NoLock();
        }

        NotifyChanged(options.Key);
        AppDiagnostics.LogInfo("runtime-jobs.cancelled", $"{options.Key}: {message}");
    }

    private static void DispatchToast(BackgroundRuntimeJobOptions options, bool success, string? errorMessage)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (success)
            {
                ToastWindow.Show(options.SuccessTitle, options.SuccessBody);
            }
            else
            {
                ToastWindow.ShowError(options.FailureTitle, BuildRuntimeJobFailureToastBody(options, errorMessage));
            }
        }));
    }

    private static string BuildRuntimeJobFailureToastBody(BackgroundRuntimeJobOptions options, string? errorMessage)
    {
        var details = BuildRuntimeJobToastDetail(errorMessage);
        var recovery = options.FailureTitle.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            ? $"Close anything using {options.Label}, then retry from Settings."
            : $"Check Settings and retry {options.Label}.";

        return $"{recovery}\n{details}";
    }

    private static string BuildRuntimeJobToastDetail(string? errorMessage)
    {
        var detail = NormalizeRuntimeJobText(errorMessage, "Unknown error.");

        return detail.Length <= MaxRuntimeJobToastDetailLength
            ? detail
            : detail[..(MaxRuntimeJobToastDetailLength - 3)].TrimEnd() + "...\nDetails were shortened; check Settings or logs for the full output.";
    }

    private static string BuildRuntimeJobProgressStatus(string? message, string fallbackStatus)
    {
        var fallback = string.IsNullOrWhiteSpace(fallbackStatus) ? "Working..." : fallbackStatus;
        var status = NormalizeRuntimeJobText(message, fallback);

        return status.Length <= MaxRuntimeJobStatusLength
            ? status
            : status[..(MaxRuntimeJobStatusLength - 3)].TrimEnd() + "...";
    }

    private static string NormalizeRuntimeJobText(string? text, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(text) ? fallback : text;
        var normalized = string.Join(' ', source.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static void EnsureInitialized_NoLock()
    {
        if (_initialized)
            return;

        try
        {
            LoadPersisted_NoLock();
            NormalizeInterruptedJobs_NoLock();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("runtime-jobs.initialize", ex);
        }

        _initialized = true;
    }

    private static void LoadPersisted_NoLock()
    {
        Jobs.Clear();
        if (!File.Exists(PersistPath))
            return;

        try
        {
            var persisted = JsonSerializer.Deserialize<List<PersistedJobState>>(File.ReadAllText(PersistPath), JsonOptions) ?? new();
            foreach (var item in persisted)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;

                Jobs[item.Key] = new JobState
                {
                    Key = item.Key,
                    Label = item.Label,
                    IsRunning = item.IsRunning,
                    Status = item.Status,
                    LastSucceeded = item.LastSucceeded,
                    LastError = item.LastError
                };
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("runtime-jobs.load", ex);
            Jobs.Clear();
            TryQuarantinePersistedJobsFile();
        }
    }

    private static void TryQuarantinePersistedJobsFile()
    {
        try
        {
            if (!File.Exists(PersistPath))
                return;

            var quarantinePath = PersistPath + ".bad";
            File.Move(PersistPath, quarantinePath, overwrite: true);
            AppDiagnostics.LogWarning(
                "runtime-jobs.quarantine",
                $"Moved unreadable runtime job state to {Path.GetFileName(quarantinePath)}.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "runtime-jobs.quarantine",
                $"Failed to quarantine unreadable runtime job state file {Path.GetFileName(PersistPath)}: {ex.Message}",
                ex);
        }
    }

    private static void NormalizeInterruptedJobs_NoLock()
    {
        bool changed = false;
        foreach (var state in Jobs.Values)
        {
            if (!state.IsRunning)
                continue;

            state.IsRunning = false;
            state.LastSucceeded = false;
            state.LastError = BuildInterruptedRuntimeJobMessage(state.Label);
            state.Status = "Interrupted. Retry from Settings.";
            changed = true;
        }

        if (changed)
            Persist_NoLock();
    }

    private static string BuildInterruptedRuntimeJobMessage(string label)
    {
        var jobLabel = string.IsNullOrWhiteSpace(label) ? "runtime setup" : label;
        return $"{jobLabel} was interrupted because OddSnap closed before it finished. Retry from Settings.";
    }

    private static string BuildCancelledRuntimeJobMessage(string label)
    {
        var jobLabel = string.IsNullOrWhiteSpace(label) ? "runtime setup" : label;
        return $"{jobLabel} was cancelled before it finished. Retry from Settings.";
    }

    private static void Persist_NoLock()
    {
        var tempPath = PersistPath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(PersistPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var persisted = Jobs.Values
                .OrderBy(state => state.Key, StringComparer.Ordinal)
                .Select(state => new PersistedJobState
                {
                    Key = state.Key,
                    Label = state.Label,
                    IsRunning = state.IsRunning,
                    Status = state.Status,
                    LastSucceeded = state.LastSucceeded,
                    LastError = state.LastError
                })
                .ToList();

            File.WriteAllText(tempPath, JsonSerializer.Serialize(persisted, JsonOptions));
            File.Move(tempPath, PersistPath, overwrite: true);
            _persistFailureToastShown = false;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("runtime-jobs.persist", ex);
            TryDeletePersistTempFile(tempPath);
            DispatchRuntimeJobPersistenceWarningToast();
        }
    }

    private static void DispatchRuntimeJobPersistenceWarningToast()
    {
        if (_persistFailureToastShown)
            return;

        _persistFailureToastShown = true;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ToastWindow.ShowError(
                "Runtime status not saved",
                "Runtime setup can continue, but its status may not survive restart. Check Settings and retry if needed.");
        }));
    }

    private static void TryDeletePersistTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "runtime-jobs.temp-cleanup",
                $"Failed to delete temporary runtime job state file {Path.GetFileName(tempPath)}: {ex.Message}",
                ex);
        }
    }

    private static BackgroundRuntimeJobSnapshot ToSnapshot(JobState state)
        => new(state.Key, state.Label, state.IsRunning, state.Status, state.LastSucceeded, state.LastError);

    private static AppJobSnapshot ToAppJobSnapshot(JobState state)
        => new(state.Key, state.Label, AppJobArea.Runtime, state.IsRunning, state.Status, state.LastSucceeded, state.LastError);

    private static void NotifyChanged(string key)
    {
        try
        {
            Changed?.Invoke(key);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("runtime-jobs.changed", ex);
        }
    }

    public static void CancelAllRunningJobs()
    {
        lock (Gate)
        {
            foreach (var state in Jobs.Values)
            {
                try { state.Cancellation?.Cancel(); } catch { }
            }
        }
    }
}
