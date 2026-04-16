namespace Yoink.AppModel.Jobs;

public enum AppJobArea
{
    Runtime,
    Indexing,
    Upload,
    SettingsMigration
}

public sealed record AppJobSnapshot(
    string Key,
    string Label,
    AppJobArea Area,
    bool IsRunning,
    string Status,
    bool? LastSucceeded,
    string? LastError);
