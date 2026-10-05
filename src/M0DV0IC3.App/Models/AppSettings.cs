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

    public bool MinimizeToTray { get; set; } = true;

    public bool StartMinimized { get; set; }

    /// <summary>Ya se avisó de que al cerrar la ventana la app sigue en la bandeja.</summary>
    public bool TrayTipShown { get; set; }

    public List<VoicePreset> CustomVoices { get; set; } = [];

    public List<SoundEntry> Sounds { get; set; } = [];

    /// <summary>Versión del catálogo de sonidos incluidos que ya se añadió (<see cref="BuiltInSounds.Version"/>).</summary>
    public int BuiltInSoundsVersion { get; set; }

    /// <summary>Acción → combinación ("Ctrl+Alt+V"). Una cadena vacía significa "sin atajo" (no se usa el de serie).</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];

    /// <summary>Corrige valores fuera de rango o nulos de un archivo editado a mano.</summary>
    public void Normalize()
    {
        CustomVoices ??= [];
        Sounds ??= [];
        Hotkeys ??= [];
        CustomVoices.RemoveAll(v => v is null);
        Sounds.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Id) || (string.IsNullOrWhiteSpace(s.FileName) && BuiltInSounds.Find(s.BuiltIn) is null));
        if (!Enum.IsDefined(VoiceRange)) VoiceRange = VoiceRange.Medium;
        SafetyMarginMs = double.IsFinite(SafetyMarginMs) ? Math.Clamp(SafetyMarginMs, 0.5, 20) : 2;
        GateThresholdDb = double.IsFinite(GateThresholdDb) ? Math.Clamp(GateThresholdDb, NoiseGate.DisabledThresholdDb, -20) : NoiseGate.DisabledThresholdDb;
        SoundboardVolume = double.IsFinite(SoundboardVolume) ? Math.Clamp(SoundboardVolume, 0, 2) : 1;
        RandomVoiceSeconds = double.IsFinite(RandomVoiceSeconds) ? Math.Clamp(RandomVoiceSeconds, 0, 120) : 5;
        AppAudioVolumeDb = double.IsFinite(AppAudioVolumeDb) ? Math.Clamp(AppAudioVolumeDb, -40, 6) : -15;
        foreach (var sound in Sounds)
        {
            sound.Name ??= "Sonido";
            sound.Volume = double.IsFinite(sound.Volume) ? Math.Clamp(sound.Volume, 0, 2) : 1;
        }
    }
}

/// <summary>
/// Un sonido del soundboard. Los del usuario están copiados en %AppData%\M0DV0IC3\sounds; los incluidos se leen
/// de la carpeta Sonidos de la app.
/// </summary>
public sealed class SoundEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>Nombre del archivo dentro de la carpeta sounds (vacío en los incluidos).</summary>
    public string FileName { get; set; } = "";

    /// <summary>Sonido incluido: su <see cref="BuiltInSound.Path"/>. Quitarlo no borra el archivo.</summary>
    public string? BuiltIn { get; set; }

    public double Volume { get; set; } = 1.0;

    public string? Hotkey { get; set; }
}
