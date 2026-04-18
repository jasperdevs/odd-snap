using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OddSnap.Native;

internal static partial class ProcessMemory
{
    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(nint hProcess);

    public static void TrimCurrentProcessWorkingSet()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);
        }
        catch { }
    }
}
