using System.Runtime.InteropServices;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>Foto de los procesos en marcha (pid → padre y ejecutable), con la API Toolhelp32 de Windows.</summary>
internal sealed unsafe partial class ProcessTree
{
    private const uint SnapProcess = 0x2;
    private static readonly nint InvalidHandle = -1;

    private readonly Dictionary<uint, (uint Parent, string Exe)> _entries = [];

    private ProcessTree()
    {
    }

    public static ProcessTree Snapshot()
    {
        var tree = new ProcessTree();
        nint snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle) return tree;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
            for (bool ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                tree._entries[entry.ProcessId] = (entry.ParentProcessId, new string(entry.ExeFile));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return tree;
    }

    /// <summary>"Spotify.exe", o null si el proceso ya no existe.</summary>
    public string? ExeName(uint pid) => _entries.TryGetValue(pid, out var entry) ? entry.Exe : null;

    public IEnumerable<uint> FindByExe(string exe) =>
        _entries.Where(e => string.Equals(e.Value.Exe, exe, StringComparison.OrdinalIgnoreCase)).Select(e => e.Key);

    /// <summary>
    /// Sube por los padres mientras sean el mismo ejecutable: el proceso principal del navegador o de Spotify,
    /// no el proceso auxiliar que reproduce el audio. Capturando la raíz con sus hijos, se sigue oyendo aunque la
    /// app reinicie ese proceso auxiliar.
    /// </summary>
    public uint FindRoot(uint pid)
    {
        uint current = pid;
        for (int depth = 0; depth < 16 && _entries.TryGetValue(current, out var entry); depth++)
        {
            if (entry.Parent == current || !_entries.TryGetValue(entry.Parent, out var parent)) break;
            if (!string.Equals(parent.Exe, entry.Exe, StringComparison.OrdinalIgnoreCase)) break;
            current = entry.Parent;
        }
        return current;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
