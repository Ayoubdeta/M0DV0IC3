using System.Runtime.InteropServices;

namespace M0DV0IC3.Audio.Realtime;

/// <summary>
/// Registra el hilo actual en el Multimedia Class Scheduler Service ("Pro Audio"). Windows le da
/// prioridad casi de tiempo real y evita que otros procesos le roben CPU (menos cortes con buffers pequeños).
/// </summary>
internal static partial class Mmcss
{
    private const int AvrtPriorityHigh = 1;

    public static IntPtr Enter(string taskName = "Pro Audio")
    {
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        uint taskIndex = 0;
        IntPtr handle = AvSetMmThreadCharacteristics(taskName, ref taskIndex);
        if (handle != IntPtr.Zero) AvSetMmThreadPriority(handle, AvrtPriorityHigh);
        return handle;
    }

    public static void Leave(IntPtr handle)
    {
        if (handle != IntPtr.Zero) AvRevertMmThreadCharacteristics(handle);
    }

    [LibraryImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [LibraryImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AvSetMmThreadPriority(IntPtr handle, int priority);

    [LibraryImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
