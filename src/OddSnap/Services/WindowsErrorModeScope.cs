using OddSnap.Native;

namespace OddSnap.Services;

internal readonly struct WindowsErrorModeScope : IDisposable
{
    private readonly uint _previousMode;

    private WindowsErrorModeScope(uint previousMode)
    {
        _previousMode = previousMode;
    }

    public static WindowsErrorModeScope SuppressSystemDialogs()
    {
        var flags = Kernel32.SEM_FAILCRITICALERRORS |
                    Kernel32.SEM_NOGPFAULTERRORBOX |
                    Kernel32.SEM_NOOPENFILEERRORBOX;
        var previous = Kernel32.SetErrorMode(flags);
        return new WindowsErrorModeScope(previous);
    }

    public void Dispose()
    {
        Kernel32.SetErrorMode(_previousMode);
    }
}
