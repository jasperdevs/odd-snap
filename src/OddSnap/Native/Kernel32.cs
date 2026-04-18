using System.Runtime.InteropServices;

namespace OddSnap.Native;

internal static partial class Kernel32
{
    internal const uint SEM_FAILCRITICALERRORS = 0x0001;
    internal const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    internal const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [LibraryImport("kernel32.dll")]
    internal static partial uint SetErrorMode(uint uMode);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();
}
