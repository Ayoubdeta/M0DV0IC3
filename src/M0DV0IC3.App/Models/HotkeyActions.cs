namespace M0DV0IC3.App.Models;

public sealed record HotkeyActionInfo(string Id, string Label, string? DefaultGesture);

/// <summary>Acciones globales con atajo de teclado. Los sonidos usan además "Sound:{id}".</summary>
public static class HotkeyActions
{
    public const string ToggleVoice = "ToggleVoice";
    public const string ToggleMute = "ToggleMute";
    public const string ToggleMonitor = "ToggleMonitor";
    public const string ToggleNoise = "ToggleNoise";
    public const string PrevVoice = "PrevVoice";
    public const string NextVoice = "NextVoice";
    public const string ToggleRandomVoice = "ToggleRandomVoice";
    public const string ToggleAppAudio = "ToggleAppAudio";
    public const string StopSounds = "StopSounds";
    public const string SoundPrefix = "Sound:";

    private const string VoicePrefix = "Voice";

    public static IReadOnlyList<HotkeyActionInfo> All { get; } = Build();

    public static string VoiceAction(int number) => VoicePrefix + number;

    public static string SoundAction(string soundId) => SoundPrefix + soundId;

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
            new(ToggleVoice, "Activar / desactivar la voz", "Ctrl+Alt+V"),
            new(ToggleMute, "Silenciar / reactivar el micrófono", "Ctrl+Alt+X"),
            new(ToggleMonitor, "Escucharme (activar / desactivar)", "Ctrl+Alt+M"),
            new(ToggleNoise, "Supresión de ruido (activar / desactivar)", "Ctrl+Alt+N"),
            new(PrevVoice, "Voz anterior", "Ctrl+Alt+Left"),
            new(NextVoice, "Voz siguiente", "Ctrl+Alt+Right"),
            new(ToggleRandomVoice, "Voz aleatoria (activar / desactivar)", "Ctrl+Alt+R"),
            new(ToggleAppAudio, "Música por el micro (activar / desactivar)", "Ctrl+Alt+P"),
        };
        for (int n = 1; n <= 9; n++)
            list.Add(new(VoiceAction(n), $"Elegir la voz n.º {n} de la lista", $"Ctrl+Alt+{n}"));
        // El + del teclado numérico, sin Ctrl ni Alt: así se para un sonido largo con una sola tecla. El + del teclado
        // principal no se puede usar solo, porque dejaría de escribirse "+" en todas las apps.
        list.Add(new(StopSounds, "Parar todos los sonidos", "Add"));
        return list;
    }
}
