using System.Threading;

namespace OddSnap.UI;

internal sealed class OcrResultWindowLifecycle
{
    private int _closeRequested;

    public bool IsCloseRequested => Volatile.Read(ref _closeRequested) == 1;

    public bool TryBeginClose() => Interlocked.Exchange(ref _closeRequested, 1) == 0;

    public bool ShouldCloseOnDeactivate(bool isLoaded, bool isMinimized) =>
        isLoaded && !isMinimized && !IsCloseRequested;
}
