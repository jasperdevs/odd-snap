using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Yoink.UI;

namespace Yoink.Services;

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
    private sealed class JobState
    {
        public string Key { get; init; } = "";
        public string Label { get; set; } = "";
        public bool IsRunning { get; set; }
        public string Status { get; set; } = "";
        public bool? LastSucceeded { get; set; }
        public string? LastError { get; set; }
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
        "Yoink",
        "runtime-jobs.json");
    private static bool _initialized;

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
        lock (Gate)
        {
            EnsureInitialized_NoLock();
            if (Jobs.TryGetValue(options.Key, out var existing) && existing.IsRunning)
                return false;

            Jobs[options.Key] = new JobState
            {
                Key = options.Key,
                Label = options.Label,
                IsRunning = true,
                Status = options.StartingStatus,
                LastSucceeded = null,
                LastError = null
            };
            Persist_NoLock();
        }

        AppDiagnostics.LogInfo("runtime-jobs.start", $"{options.Key}: {options.StartingStatus}");
        NotifyChanged(options.Key);

        _ = Task.Run(async () =>
        {
            var progress = new Progress<string>(message =>
            {
                UpdateStatus(options.Key, string.IsNullOrWhiteSpace(message) ? options.StartingStatus : message);
            });

            try
            {
                await work(progress, CancellationToken.None).ConfigureAwait(false);
                Complete(options, success: true, error: null);
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
                : $"Failed: {errorMessage}";
            Persist_NoLock();
        }

        NotifyChanged(options.Key);
        if (success)
            AppDiagnostics.LogInfo("runtime-jobs.complete", $"{options.Key}: {options.SuccessStatus ?? "Ready"}");
        else
            AppDiagnostics.LogWarning("runtime-jobs.complete", $"{options.Key}: {errorMessage ?? "Unknown error"}");
        DispatchToast(options, success, errorMessage);
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
                ToastWindow.ShowError(options.FailureTitle, string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error." : errorMessage);
            }
        }));
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
            state.LastError = "Interrupted because Yoink closed before setup finished.";
            state.Status = "Interrupted - retry setup";
            changed = true;
        }

        if (changed)
            Persist_NoLock();
    }

    private static void Persist_NoLock()
    {
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

            var tempPath = PersistPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(persisted, JsonOptions));
            File.Move(tempPath, PersistPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("runtime-jobs.persist", ex);
        }
    }

    private static BackgroundRuntimeJobSnapshot ToSnapshot(JobState state)
        => new(state.Key, state.Label, state.IsRunning, state.Status, state.LastSucceeded, state.LastError);

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
}
