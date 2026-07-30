namespace OddSnap.Capture;

internal static class OverlayPlacement
{
    public static (Point Position, int Index) Resolve(
        IReadOnlyList<Point> candidates,
        Size clientSize,
        Size overlaySize,
        int margin,
        Rectangle avoidRect,
        int preferredIndex)
    {
        if (TryResolveCandidate(preferredIndex, candidates, clientSize, overlaySize, margin, avoidRect, out var preferred))
            return (preferred, preferredIndex);

        for (int i = 0; i < candidates.Count; i++)
        {
            if (i == preferredIndex)
                continue;

            if (TryResolveCandidate(i, candidates, clientSize, overlaySize, margin, avoidRect, out var resolved))
                return (resolved, i);
        }

        return (Clamp(candidates[0], clientSize, overlaySize, margin), 0);
    }

    private static bool TryResolveCandidate(
        int index,
        IReadOnlyList<Point> candidates,
        Size clientSize,
        Size overlaySize,
        int margin,
        Rectangle avoidRect,
        out Point resolved)
    {
        resolved = Point.Empty;
        if ((uint)index >= (uint)candidates.Count)
            return false;

        var candidate = candidates[index];
        if (candidate == Point.Empty)
            return false;

        var clamped = Clamp(candidate, clientSize, overlaySize, margin);
        var rect = new Rectangle(clamped, overlaySize);
        if (!avoidRect.IsEmpty && rect.IntersectsWith(avoidRect))
            return false;

        resolved = clamped;
        return true;
    }

    private static Point Clamp(Point point, Size clientSize, Size overlaySize, int margin)
        => new(
            Math.Clamp(point.X, margin, Math.Max(margin, clientSize.Width - overlaySize.Width - margin)),
            Math.Clamp(point.Y, margin, Math.Max(margin, clientSize.Height - overlaySize.Height - margin)));
}
