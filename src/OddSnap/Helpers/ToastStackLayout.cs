namespace OddSnap.Helpers;

internal static class ToastStackLayout
{
    public static double GetOffset(IEnumerable<double> retainedHeights, double gap)
    {
        ArgumentNullException.ThrowIfNull(retainedHeights);

        double safeGap = Normalize(gap);
        double offset = 0;
        int count = 0;
        foreach (double height in retainedHeights)
        {
            offset += Normalize(height);
            count++;
        }

        return offset + (Math.Max(0, count) * safeGap);
    }

    public static int GetOldestEvictionCount(
        IReadOnlyList<double> retainedHeights,
        double incomingHeight,
        double availableHeight,
        double gap)
    {
        ArgumentNullException.ThrowIfNull(retainedHeights);

        double safeIncomingHeight = Normalize(incomingHeight);
        double safeAvailableHeight = Normalize(availableHeight);
        double safeGap = Normalize(gap);
        double totalHeight = safeIncomingHeight;
        foreach (double height in retainedHeights)
            totalHeight += Normalize(height) + safeGap;

        int evictionCount = 0;
        while (evictionCount < retainedHeights.Count && totalHeight > safeAvailableHeight)
        {
            totalHeight -= Normalize(retainedHeights[evictionCount]) + safeGap;
            evictionCount++;
        }

        return evictionCount;
    }

    private static double Normalize(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;
}
