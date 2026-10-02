using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>Una app que reproduce (o puede reproducir) audio: Spotify, el navegador, un juego...</summary>
/// <param name="ProcessId">Proceso raíz de la app: se captura junto con todos sus procesos hijos.</param>
/// <param name="ExeName">Ejecutable sin extensión ("Spotify"): sirve para volver a encontrarla si se reinicia.</param>
/// <param name="DisplayName">Nombre para mostrar ("Spotify", "Brave Browser").</param>
/// <param name="IsPlaying">Está sonando ahora mismo.</param>
public sealed record AudioApp(uint ProcessId, string ExeName, string DisplayName, bool IsPlaying)
{
    public string Display => IsPlaying ? $"{DisplayName} (sonando)" : DisplayName;
}

public static class AudioAppFinder
{
    /// <summary>Apps de música que se ofrecen aunque aún no tengan sesión de audio (abiertas, pero sin haber sonado).</summary>
    private static readonly string[] KnownMusicApps = ["Spotify"];

    /// <summary>Transmitir Discord haría que tus amigos se oyeran a sí mismos con eco: no se ofrece.</summary>
    private static readonly string[] Excluded = ["Discord", "DiscordPTB", "DiscordCanary"];

    /// <summary>
    /// Apps con una sesión de audio en alguna salida activa, más las de música conocidas que estén abiertas.
    /// Sin la propia app ni los sonidos del sistema. Primero las que están sonando.
    /// </summary>
    public static IReadOnlyList<AudioApp> List()
    {
        var tree = ProcessTree.Snapshot();
        uint self = (uint)Environment.ProcessId;
        var apps = new Dictionary<uint, AudioApp>();

        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    using var manager = device.AudioSessionManager;
                    using var sessions = manager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (session.IsSystemSoundsSession || session.GetProcessID == 0) continue;
                        uint root = tree.FindRoot(session.GetProcessID);
                        if (root == self) continue;
                        bool playing = session.State == AudioSessionState.AudioSessionStateActive;
                        if (apps.TryGetValue(root, out var known))
                        {
                            if (playing && !known.IsPlaying) apps[root] = known with { IsPlaying = true };
                        }
                        else if (Describe(root, tree, playing) is { } app && !IsExcluded(app))
                        {
                            apps[root] = app;
                        }
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                    // Un dispositivo que desaparece mientras se recorre: se ignora.
                }
            }
        }

        foreach (string exe in KnownMusicApps)
        {
            if (apps.Values.Any(a => a.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase))) continue;
            if (FindByExe(exe, tree) is { } app) apps[app.ProcessId] = app;
        }

        return apps.Values
            .OrderByDescending(a => a.IsPlaying)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsExcluded(AudioApp app) => Excluded.Contains(app.ExeName, StringComparer.OrdinalIgnoreCase);

    /// <summary>La app con ese ejecutable que esté abierta ahora (su proceso raíz), o null. Sirve tras un reinicio de la app.</summary>
    public static AudioApp? FindByExe(string exeName) => FindByExe(exeName, ProcessTree.Snapshot());

    /// <summary>El proceso sigue abierto.</summary>
    public static bool IsAlive(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static AudioApp? FindByExe(string exeName, ProcessTree tree)
    {
        string exe = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe";
        uint root = tree.FindByExe(exe).Select(tree.FindRoot).Distinct().FirstOrDefault();
        return root == 0 ? null : Describe(root, tree, playing: false);
    }

    private static AudioApp? Describe(uint pid, ProcessTree tree, bool playing)
    {
        string? exe = tree.ExeName(pid);
        if (exe is null) return null;
        string name = Path.GetFileNameWithoutExtension(exe);
        string display = name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string? description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) display = description.Trim();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Procesos protegidos o de otro usuario: basta con el nombre del ejecutable.
        }
        return new AudioApp(pid, name, display, playing);
    }
}
