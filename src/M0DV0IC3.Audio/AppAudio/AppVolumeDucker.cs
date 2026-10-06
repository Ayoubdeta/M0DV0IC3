using NAudio.CoreAudioApi;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>
/// Baja el volumen de una app (todas sus sesiones de audio, en todas las salidas) en el mezclador de Windows, y lo
/// restaura. Lo usa el karaoke: Spotify no debe sonar con voz en tus cascos, pero la captura de una app recibe el
/// audio ya con su volumen aplicado (silenciarla del todo la silenciaría también para la app). Al 0,1 % no se oye
/// y, como la captura es en coma flotante, multiplicarla por 1000 la deja como estaba.
/// <para>Windows guarda el volumen de cada app entre sesiones: hay que restaurarlo siempre (la app lo apunta en sus
/// ajustes por si se cierra de golpe).</para>
/// </summary>
public sealed class AppVolumeDucker
{
    public const float DuckedLevel = 0.001f;

    private readonly Dictionary<string, float> _originals = [];

    /// <summary>Volumen que tenía la app (el mayor de sus sesiones), para compensar la captura y restaurarlo.</summary>
    public float OriginalVolume { get; private set; } = 1f;

    public bool IsDucked => _originals.Count > 0;

    /// <summary>
    /// Baja las sesiones de la app (y de sus procesos hijos) al <see cref="DuckedLevel"/>. Se puede llamar cada poco:
    /// recoge sesiones nuevas y, si alguien ha cambiado el volumen de la app, lo toma como el nuevo original.
    /// </summary>
    public void Duck(uint rootProcessId)
    {
        var tree = ProcessTree.Snapshot();
        ForEachSession(tree, rootProcessId, (id, volume) =>
        {
            float current = volume.Volume;
            if (!_originals.ContainsKey(id) || current > DuckedLevel * 1.5f) _originals[id] = current;
            if (Math.Abs(current - DuckedLevel) > DuckedLevel * 0.01f) volume.Volume = DuckedLevel;
        });
        if (_originals.Count > 0) OriginalVolume = Math.Max(DuckedLevel, _originals.Values.Max());
    }

    /// <summary>Devuelve a cada sesión su volumen. Las que no se conocían (p. ej., tras un cierre de golpe) van a <paramref name="fallback"/>.</summary>
    public void Restore(uint rootProcessId, float? fallback = null)
    {
        var tree = ProcessTree.Snapshot();
        ForEachSession(tree, rootProcessId, (id, volume) =>
        {
            if (_originals.TryGetValue(id, out float original)) volume.Volume = original;
            else if (fallback is { } level && volume.Volume <= DuckedLevel * 1.5f) volume.Volume = level;
        });
        _originals.Clear();
    }

    private static void ForEachSession(ProcessTree tree, uint rootProcessId, Action<string, SimpleAudioVolume> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    uint pid = session.GetProcessID;
                    if (pid == 0 || tree.FindRoot(pid) != rootProcessId) continue;
                    action(device.ID + "|" + session.GetSessionInstanceIdentifier, session.SimpleAudioVolume);
                }
            }
        }
    }
}
