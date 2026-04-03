namespace Yoink.Helpers;

public static class ToastPinPolicy
{
    public static bool CanAutoDismiss(bool isPinned, bool isHovered)
        => !isPinned && !isHovered;

    public static bool CanReplaceCurrent(bool isPinned)
        => !isPinned;
}
