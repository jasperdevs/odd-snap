namespace OddSnap.AppModel.Settings;

public enum SettingsValueKind
{
    Toggle,
    Choice,
    Text,
    Folder,
    Number,
    Duration,
    Action
}

public sealed record SettingsOptionDefinition(string Value, string Label);

public sealed record SettingDefinition(
    string Key,
    string Label,
    SettingsValueKind ValueKind,
    string Description,
    string? BindingPath = null,
    IReadOnlyList<SettingsOptionDefinition>? Options = null);

public sealed record SettingsSectionDefinition(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<SettingDefinition> Items);

public sealed record SettingsPageDefinition(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<SettingsSectionDefinition> Sections);
