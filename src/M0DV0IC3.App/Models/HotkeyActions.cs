using M0DV0IC3.App.Localization;

namespace M0DV0IC3.App.Models;

/// <summary>Grupos de la pestaña Atajos, en el orden en que se muestran.</summary>
public enum HotkeyGroup
{
    Voice,
    Sounds,
    Phrases,
    Mic,
    Music,
    PickVoice,
}

/// <param name="Label">Nombre corto de la acción.</param>
/// <param name="Detail">Qué hace, en una línea (o null).</param>
public sealed record HotkeyActionInfo(string Id, string Label, string? DefaultGesture, HotkeyGroup Group, string? Detail = null);

/// <summary>Acciones globales con atajo de teclado. Los sonidos usan además "Sound:{id}" y las frases "Phrase:{id}".</summary>
public static class HotkeyActions
{
    public const string ToggleVoice = "ToggleVoice";
    public const string ToggleMute = "ToggleMute";
    public const string ToggleMonitor = "ToggleMonitor";
    public const string ToggleNoise = "ToggleNoise";
    public const string PrevVoice = "PrevVoice";
    public const string NextVoice = "NextVoice";
    public const string ToggleRandomVoice = "ToggleRandomVoice";
    public const string HoldVoice = "HoldVoice";
    public const string ToggleAppAudio = "ToggleAppAudio";
    public const string ToggleKaraoke = "ToggleKaraoke";
    public const string ToggleRecording = "ToggleRecording";
    public const string StopSounds = "StopSounds";
    public const string SoundPrefix = "Sound:";
    public const string PhrasePrefix = "Phrase:";

    private const string VoicePrefix = "Voice";

    public static IReadOnlyList<HotkeyActionInfo> All { get; } = Build();

    public static string VoiceAction(int number) => VoicePrefix + number;

    public static string SoundAction(string soundId) => SoundPrefix + soundId;

    public static string PhraseAction(string phraseId) => PhrasePrefix + phraseId;

    /// <summary>"Voice3" → 2 (posición en la lista de voces).</summary>
    public static bool TryGetVoiceIndex(string actionId, out int index)
    {
        index = -1;
        if (actionId.Length != VoicePrefix.Length + 1 || !actionId.StartsWith(VoicePrefix, StringComparison.Ordinal)) return false;
        char digit = actionId[^1];
        if (digit is < '1' or > '9') return false;
        index = digit - '1';
        return true;
    }

    private static List<HotkeyActionInfo> Build()
    {
        var list = new List<HotkeyActionInfo>
        {
            new(ToggleVoice, Loc.T("Activar / desactivar la voz"), "Ctrl+Alt+V", HotkeyGroup.Voice, Loc.T("El interruptor VOZ ON de arriba")),
            // El - del teclado numérico, sin Ctrl ni Alt: se mantiene pulsado con un dedo en mitad de una partida.
            new(HoldVoice, Loc.T("Mantener pulsado para cambiar de voz"), "Subtract", HotkeyGroup.Voice),
            new(PrevVoice, Loc.T("Voz anterior"), "Ctrl+Alt+Left", HotkeyGroup.Voice, Loc.T("Con el filtro de favoritas, solo entre ellas")),
            new(NextVoice, Loc.T("Voz siguiente"), "Ctrl+Alt+Right", HotkeyGroup.Voice, Loc.T("Con el filtro de favoritas, solo entre ellas")),
            new(ToggleRandomVoice, Loc.T("Voz aleatoria"), "Ctrl+Alt+R", HotkeyGroup.Voice, Loc.T("Cambia sola de voz cada pocos segundos")),
            // El + del teclado numérico, sin Ctrl ni Alt: así se para un sonido largo con una sola tecla. El + del teclado
            // principal no se puede usar solo, porque dejaría de escribirse "+" en todas las apps.
            new(StopSounds, Loc.T("Parar sonidos y frases"), "Add", HotkeyGroup.Sounds, Loc.T("Corta todo lo que esté sonando")),
            new(ToggleMute, Loc.T("Silenciar el micro"), "Ctrl+Alt+X", HotkeyGroup.Mic, Loc.T("Nadie te oye: ni tu voz, ni los sonidos, ni la música")),
            new(ToggleMonitor, Loc.T("Escucharme"), "Ctrl+Alt+M", HotkeyGroup.Mic, Loc.T("Oír tu voz cambiada en los cascos")),
            new(ToggleNoise, Loc.T("Supresión de ruido"), "Ctrl+Alt+N", HotkeyGroup.Mic, Loc.T("Quita el ruido de fondo (teclado, ventilador)")),
            new(ToggleAppAudio, Loc.T("Música por el micro"), "Ctrl+Alt+P", HotkeyGroup.Music, Loc.T("Lo que suena en Spotify llega a Discord con tu voz")),
            new(ToggleKaraoke, Loc.T("Modo karaoke"), "Ctrl+Alt+K", HotkeyGroup.Music, Loc.T("Quita la voz de la canción y muestra la letra")),
            new(ToggleRecording, Loc.T("Grabar"), "Ctrl+Alt+G", HotkeyGroup.Music, Loc.T("Lo que sale por el micro, en MP3 en Música\\M0DV0IC3")),
        };
        for (int n = 1; n <= 9; n++)
            list.Add(new(VoiceAction(n), Loc.F("Voz {0}", n), $"Ctrl+Alt+{n}", HotkeyGroup.PickVoice));
        return list;
    }
}
