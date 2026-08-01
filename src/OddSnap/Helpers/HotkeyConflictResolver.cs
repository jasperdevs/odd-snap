using OddSnap.Models;

namespace OddSnap.Helpers;

internal sealed record HotkeyConflict(string ToolId, string Label, bool IsAiRedirect);

internal static class HotkeyConflictResolver
{
    public static HotkeyConflict? Find(AppSettings settings, string currentToolId, uint modifiers, uint key)
    {
        if (key == 0)
            return null;

        foreach (var tool in ToolDef.AllTools.Concat(ToolDef.ToolbarActions).Concat(ToolDef.HotkeyOnlyActions))
        {
            if (string.Equals(tool.Id, currentToolId, StringComparison.OrdinalIgnoreCase))
                continue;

            var (existingModifiers, existingKey) = settings.GetToolHotkey(tool.Id);
            if (existingModifiers == modifiers && existingKey == key)
                return new HotkeyConflict(tool.Id, tool.Label, IsAiRedirect: false);
        }

        if (settings.AiRedirectHotkeyKey != 0 &&
            settings.AiRedirectHotkeyModifiers == modifiers &&
            settings.AiRedirectHotkeyKey == key)
        {
            return new HotkeyConflict("", "AI Redirect", IsAiRedirect: true);
        }

        return null;
    }

    public static (uint Modifiers, uint Key) Clear(AppSettings settings, HotkeyConflict conflict)
    {
        if (conflict.IsAiRedirect)
        {
            var previous = (settings.AiRedirectHotkeyModifiers, settings.AiRedirectHotkeyKey);
            settings.AiRedirectHotkeyModifiers = 0;
            settings.AiRedirectHotkeyKey = 0;
            return previous;
        }

        var old = settings.GetToolHotkey(conflict.ToolId);
        settings.SetToolHotkey(conflict.ToolId, 0, 0);
        return old;
    }

    public static void Restore(
        AppSettings settings,
        HotkeyConflict conflict,
        (uint Modifiers, uint Key)? previous)
    {
        if (previous is null)
            return;

        if (conflict.IsAiRedirect)
        {
            settings.AiRedirectHotkeyModifiers = previous.Value.Modifiers;
            settings.AiRedirectHotkeyKey = previous.Value.Key;
            return;
        }

        settings.SetToolHotkey(conflict.ToolId, previous.Value.Modifiers, previous.Value.Key);
    }
}
