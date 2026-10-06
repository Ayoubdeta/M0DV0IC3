using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.App.Models;

/// <summary>Todo lo que se guarda en settings.json.</summary>
public sealed class AppSettings
{
    public string? InputDeviceId { get; set; }

    /// <summary>Solo se guarda cuando el usuario la elige a mano (o es VB-Cable).</summary>
    public string? OutputDeviceId { get; set; }

    public string? MonitorDeviceId { get; set; }

    public bool Exclusive { get; set; }

    public bool PreferLowLatency { get; set; } = true;

    public double SafetyMarginMs { get; set; } = 2;

    public VoiceRange VoiceRange { get; set; } = VoiceRange.Medium;

    /// <summary>Tono medio de tu voz aprendido en la sesión anterior (0 = aún no se sabe), para las voces con tono objetivo.</summary>
    public double LearnedPitchHz { get; set; }

    public string? SelectedVoiceId { get; set; }

    public bool VoiceEnabled { get; set; }

    public bool MonitorVoice { get; set; }

    public bool MonitorSounds { get; set; } = true;

    public bool NoiseSuppression { get; set; }

    public double GateThresholdDb { get; set; } = NoiseGate.DisabledThresholdDb;

    public double SoundboardVolume { get; set; } = 1.0;

    /// <summary>Voz aleatoria: segundos entre cambios (0 = al azar entre 2 y 10 s). No se guarda si está activada.</summary>
    public double RandomVoiceSeconds { get; set; } = 5;

    /// <summary>Música por el micro: ejecutable de la app elegida ("Spotify"). No se guarda si está transmitiendo.</summary>
    public string? AppAudioName { get; set; } = "Spotify";

    public double AppAudioVolumeDb { get; set; } = -15;

    /// <summary>Karaoke: cuánta voz se quita de la canción (0..1).</summary>
    public double KaraokeVocalStrength { get; set; } = 1.0;

    /// <summary>Karaoke: adelanto (positivo) o retraso de la letra, en segundos.</summary>
    public double KaraokeLyricsOffset { get; set; }

    /// <summary>
    /// Volumen que tenía Spotify antes del karaoke, mientras está bajado (0 = no está bajado). Si la app se cierra de
    /// golpe, al volver a abrirla se le devuelve: Windows guarda el volumen de cada app.
    /// </summary>
    public double KaraokeRestoreVolume { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool StartMinimized { get; set; }

    /// <summary>Ya se avisó de que al cerrar la ventana la app sigue en la bandeja.</summary>
    public bool TrayTipShown { get; set; }

    public List<VoicePreset> CustomVoices { get; set; } = [];

    /// <summary>Ids de las voces marcadas con la estrella.</summary>
    public List<string> FavoriteVoices { get; set; } = [];

    /// <summary>La pestaña Voces muestra solo las favoritas.</summary>
    public bool FavoritesOnly { get; set; }

    public List<SoundEntry> Sounds { get; set; } = [];

    /// <summary>Acción → combinación ("Ctrl+Alt+V"). Una cadena vacía significa "sin atajo" (no se usa el de serie).</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];

    /// <summary>Cambios de atajos de serie ya aplicados a los ajustes guardados.</summary>
    public int HotkeysVersion { get; set; }

    /// <summary>Corrige valores fuera de rango o nulos de un archivo editado a mano.</summary>
    public void Normalize()
    {
        CustomVoices ??= [];
        FavoriteVoices ??= [];
        FavoriteVoices.RemoveAll(string.IsNullOrWhiteSpace);
        Sounds ??= [];
        Hotkeys ??= [];
        if (HotkeysVersion < 1)
        {
            // v1.1: "Parar todos los sonidos" pasa al + del teclado numérico. Si estaba con el atajo antiguo o quitado,
            // se pone el nuevo una vez (lo pidió quien lo usa: parar un sonido largo sin esperar a que acabe).
            if (Hotkeys.TryGetValue(HotkeyActions.StopSounds, out string? stop) && stop is "" or "Ctrl+Alt+S")
                Hotkeys.Remove(HotkeyActions.StopSounds);
            HotkeysVersion = 1;
        }
        CustomVoices.RemoveAll(v => v is null);
        Sounds.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.FileName));
        if (!Enum.IsDefined(VoiceRange)) VoiceRange = VoiceRange.Medium;
        SafetyMarginMs = double.IsFinite(SafetyMarginMs) ? Math.Clamp(SafetyMarginMs, 0.5, 20) : 2;
        GateThresholdDb = double.IsFinite(GateThresholdDb) ? Math.Clamp(GateThresholdDb, NoiseGate.DisabledThresholdDb, -20) : NoiseGate.DisabledThresholdDb;
        SoundboardVolume = double.IsFinite(SoundboardVolume) ? Math.Clamp(SoundboardVolume, 0, 2) : 1;
        RandomVoiceSeconds = double.IsFinite(RandomVoiceSeconds) ? Math.Clamp(RandomVoiceSeconds, 0, 120) : 5;
        AppAudioVolumeDb = double.IsFinite(AppAudioVolumeDb) ? Math.Clamp(AppAudioVolumeDb, -40, 6) : -15;
        KaraokeVocalStrength = double.IsFinite(KaraokeVocalStrength) ? Math.Clamp(KaraokeVocalStrength, 0, 1) : 1;
        KaraokeLyricsOffset = double.IsFinite(KaraokeLyricsOffset) ? Math.Clamp(KaraokeLyricsOffset, -10, 10) : 0;
        KaraokeRestoreVolume = double.IsFinite(KaraokeRestoreVolume) ? Math.Clamp(KaraokeRestoreVolume, 0, 1) : 0;
        LearnedPitchHz = double.IsFinite(LearnedPitchHz) && LearnedPitchHz is >= 40 and <= 1000 ? LearnedPitchHz : 0;
        foreach (var sound in Sounds)
        {
            sound.Name ??= "Sonido";
            sound.Volume = double.IsFinite(sound.Volume) ? Math.Clamp(sound.Volume, 0, 2) : 1;
            sound.TrimStartSeconds = double.IsFinite(sound.TrimStartSeconds) ? Math.Max(0, sound.TrimStartSeconds) : 0;
            sound.TrimEndSeconds = double.IsFinite(sound.TrimEndSeconds) ? Math.Max(0, sound.TrimEndSeconds) : 0;
        }
    }
}

/// <summary>Un sonido del soundboard. El archivo está copiado en %AppData%\M0DV0IC3\sounds.</summary>
public sealed class SoundEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>Nombre del archivo dentro de la carpeta sounds.</summary>
    public string FileName { get; set; } = "";

    public double Volume { get; set; } = 1.0;

    public string? Hotkey { get; set; }

    /// <summary>Recorte: segundo en el que empieza a sonar (0 = desde el principio). El archivo no se modifica.</summary>
    public double TrimStartSeconds { get; set; }

    /// <summary>Recorte: segundo en el que deja de sonar (0 = hasta el final).</summary>
    public double TrimEndSeconds { get; set; }
}
