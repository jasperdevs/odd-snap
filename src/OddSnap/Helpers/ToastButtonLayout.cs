using System.Windows;
using OddSnap.Models;

namespace OddSnap.Helpers;

public enum ToastButtonKind
{
    Close,
    Pin,
    Save,
    AiRedirect,
    Delete
}

public static class ToastButtonLayout
{
    public static (System.Windows.HorizontalAlignment horizontal, System.Windows.VerticalAlignment vertical, Thickness margin) ToPlacement(
        ToastButtonSlot slot,
        double inset = 8)
    {
        return slot switch
        {
            ToastButtonSlot.TopLeft => (System.Windows.HorizontalAlignment.Left, System.Windows.VerticalAlignment.Top, new Thickness(inset, inset, 0, 0)),
            ToastButtonSlot.TopInnerLeft => (System.Windows.HorizontalAlignment.Left, System.Windows.VerticalAlignment.Top, new Thickness(inset + 40, inset, 0, 0)),
            ToastButtonSlot.TopInnerRight => (System.Windows.HorizontalAlignment.Right, System.Windows.VerticalAlignment.Top, new Thickness(0, inset, inset + 40, 0)),
            ToastButtonSlot.TopRight => (System.Windows.HorizontalAlignment.Right, System.Windows.VerticalAlignment.Top, new Thickness(0, inset, inset, 0)),
            ToastButtonSlot.BottomLeft => (System.Windows.HorizontalAlignment.Left, System.Windows.VerticalAlignment.Bottom, new Thickness(inset, 0, 0, inset)),
            ToastButtonSlot.BottomInnerLeft => (System.Windows.HorizontalAlignment.Left, System.Windows.VerticalAlignment.Bottom, new Thickness(inset + 40, 0, 0, inset)),
            ToastButtonSlot.BottomInnerRight => (System.Windows.HorizontalAlignment.Right, System.Windows.VerticalAlignment.Bottom, new Thickness(0, 0, inset + 40, inset)),
            _ => (System.Windows.HorizontalAlignment.Right, System.Windows.VerticalAlignment.Bottom, new Thickness(0, 0, inset, inset))
        };
    }

    public static ToastButtonSlot GetSlot(AppSettings.ToastButtonLayoutSettings settings, ToastButtonKind button)
        => button switch
        {
            ToastButtonKind.Close => settings.CloseSlot,
            ToastButtonKind.Pin => settings.PinSlot,
            ToastButtonKind.Save => settings.SaveSlot,
            ToastButtonKind.AiRedirect => settings.AiRedirectSlot,
            _ => settings.DeleteSlot
        };

    public static bool IsVisible(AppSettings.ToastButtonLayoutSettings settings, ToastButtonKind button)
        => button switch
        {
            ToastButtonKind.Close => settings.ShowClose,
            ToastButtonKind.Pin => settings.ShowPin,
            ToastButtonKind.Save => settings.ShowSave,
            ToastButtonKind.AiRedirect => settings.ShowAiRedirect,
            _ => settings.ShowDelete
        };

    public static void SetVisible(AppSettings.ToastButtonLayoutSettings settings, ToastButtonKind button, bool visible)
    {
        switch (button)
        {
            case ToastButtonKind.Close: settings.ShowClose = visible; break;
            case ToastButtonKind.Pin: settings.ShowPin = visible; break;
            case ToastButtonKind.Save: settings.ShowSave = visible; break;
            case ToastButtonKind.AiRedirect: settings.ShowAiRedirect = visible; break;
            default: settings.ShowDelete = visible; break;
        }
    }

    public static void AssignSlot(AppSettings.ToastButtonLayoutSettings settings, ToastButtonKind button, ToastButtonSlot targetSlot)
    {
        var currentSlot = GetSlot(settings, button);
        if (currentSlot == targetSlot)
            return;

        var occupant = FindButtonAt(settings, targetSlot);
        SetSlot(settings, button, targetSlot);
        if (occupant.HasValue && occupant.Value != button)
            SetSlot(settings, occupant.Value, currentSlot);
    }

    public static ToastButtonKind? FindButtonAt(AppSettings.ToastButtonLayoutSettings settings, ToastButtonSlot slot)
    {
        if (settings.CloseSlot == slot) return ToastButtonKind.Close;
        if (settings.PinSlot == slot) return ToastButtonKind.Pin;
        if (settings.SaveSlot == slot) return ToastButtonKind.Save;
        if (settings.AiRedirectSlot == slot) return ToastButtonKind.AiRedirect;
        if (settings.DeleteSlot == slot) return ToastButtonKind.Delete;
        return null;
    }

    private static void SetSlot(AppSettings.ToastButtonLayoutSettings settings, ToastButtonKind button, ToastButtonSlot slot)
    {
        switch (button)
        {
            case ToastButtonKind.Close: settings.CloseSlot = slot; break;
            case ToastButtonKind.Pin: settings.PinSlot = slot; break;
            case ToastButtonKind.Save: settings.SaveSlot = slot; break;
            case ToastButtonKind.AiRedirect: settings.AiRedirectSlot = slot; break;
            default: settings.DeleteSlot = slot; break;
        }
    }
}
