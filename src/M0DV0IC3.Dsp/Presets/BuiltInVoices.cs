using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Dsp.Presets;

/// <summary>
/// Voces incluidas de serie: primero personajes y después efectos y ambientes. Los valores son un punto de partida;
/// <c>OutputGainDb</c> está ajustado con <c>m0dv0ic3-cli bench</c> (columna "Nivel") para que todas suenen al
/// mismo volumen que la voz original (±0,5 dB RMS). La excepción es "Lejana", que suena 3 dB más baja a propósito.
/// </summary>
public static class BuiltInVoices
{
    public static IReadOnlyList<VoicePreset> All { get; } =
    [
        new()
        {
            Id = "grave", Name = "Grave", Icon = "🐻", IsBuiltIn = true,
            PitchSemitones = -5, FormantRatio = 0.85, HighShelfDb = -2, OutputGainDb = 2,
        },
        new()
        {
            Id = "hombre", Name = "Hombre", Icon = "🧔", IsBuiltIn = true,
            PitchSemitones = -4, FormantRatio = 0.88, OutputGainDb = 1.5,
        },
        new()
        {
            Id = "mujer", Name = "Mujer", Icon = "👩", IsBuiltIn = true,
            PitchSemitones = 5, FormantRatio = 1.18, HighPassHz = 120, HighShelfDb = 2, OutputGainDb = 1.5,
        },
        new()
        {
            // Muy femenina: tono de mujer adulta (~190-210 Hz desde una voz masculina), tracto vocal más corto
            // sin llegar a sonar infantil, menos pecho, más brillo y voz con aire.
            Id = "mujer2", Name = "Mujer 2", Icon = "💃", IsBuiltIn = true,
            PitchSemitones = 8, FormantRatio = 1.2, Breathiness = 0.35,
            HighPassHz = 160, PresenceDb = 1.5, HighShelfDb = 3, OutputGainDb = 1.5,
        },
        new()
        {
            // Autotune exagerado, el de los cantantes de reggaetón y trap: corrección instantánea (la voz salta de nota
            // en nota y suena perfectamente afinada y robótica) y melodía ampliada 2,2× para que al hablar suene
            // cantado. La menor (las notas blancas del piano). Voz brillante con eco y reverb de canción.
            Id = "autotune", Name = "Autotune", Icon = "🎤", IsBuiltIn = true,
            AutotuneScale = AutotuneScale.Minor, AutotuneKey = 9, AutotuneRetuneMs = 0, AutotuneExaggeration = 0.8,
            HighPassHz = 100, PresenceDb = 2.5, HighShelfDb = 3.5,
            EchoMs = 300, EchoFeedback = 0.25, EchoMix = 0.15, ReverbMix = 0.18, ReverbSize = 0.6, OutputGainDb = 1,
        },
        new()
        {
            // El mismo autotune exagerado con la voz de "Mujer 2": se sube 8 semitonos y después se afina.
            Id = "autotune-mujer", Name = "Autotune mujer", Icon = "🎶", IsBuiltIn = true,
            PitchSemitones = 8, FormantRatio = 1.2, Breathiness = 0.2,
            AutotuneScale = AutotuneScale.Minor, AutotuneKey = 9, AutotuneRetuneMs = 0, AutotuneExaggeration = 0.8,
            HighPassHz = 160, PresenceDb = 2, HighShelfDb = 3.5,
            EchoMs = 300, EchoFeedback = 0.25, EchoMix = 0.15, ReverbMix = 0.18, ReverbSize = 0.6, OutputGainDb = 2,
        },
        new()
        {
            // Para cantar encima de una canción: corrección instantánea pero sin tocar tu melodía, y escala cromática
            // (todas las notas), que vale para cualquier canción. Con la tonalidad de la canción se nota aún más.
            Id = "autotune-cantar", Name = "Autotune cantar", Icon = "🎼", IsBuiltIn = true,
            AutotuneScale = AutotuneScale.Chromatic, AutotuneRetuneMs = 0,
            HighPassHz = 100, PresenceDb = 2.5, HighShelfDb = 3.5,
            EchoMs = 300, EchoFeedback = 0.2, EchoMix = 0.12, ReverbMix = 0.18, ReverbSize = 0.6, OutputGainDb = 0,
        },
        new()
        {
            Id = "nino", Name = "Niño", Icon = "🧒", IsBuiltIn = true,
            PitchSemitones = 6, FormantRatio = 1.25, HighPassHz = 150, HighShelfDb = 1.5, OutputGainDb = 1.5,
        },
        new()
        {
            Id = "ardilla", Name = "Pito / Ardilla", Icon = "🐿", IsBuiltIn = true,
            PitchSemitones = 8, FormantRatio = 1.35, HighPassHz = 200, OutputGainDb = 1.5,
        },
        new()
        {
            // El helio sube los formantes (el sonido viaja casi tres veces más rápido) pero apenas el tono: voz de pato.
            Id = "helio", Name = "Helio", Icon = "🎈", IsBuiltIn = true,
            PitchSemitones = 2, FormantRatio = 1.7, HighPassHz = 150, OutputGainDb = 3,
        },
        new()
        {
            Id = "robot", Name = "Robot", Icon = "🤖", IsBuiltIn = true,
            RobotHz = 110, RingModHz = 60, RingModMix = 0.35,
            CombMs = 6, CombFeedback = 0.55, CombMix = 0.45, HighPassHz = 80, OutputGainDb = 7,
        },
        new()
        {
            Id = "demonio", Name = "Demonio", Icon = "😈", IsBuiltIn = true,
            PitchSemitones = -8, FormantRatio = 0.75, Distortion = 0.25, LowPassHz = 6000,
            ReverbMix = 0.25, ReverbSize = 0.75, OutputGainDb = -3,
        },
        new()
        {
            Id = "alien", Name = "Alien", Icon = "👽", IsBuiltIn = true,
            PitchSemitones = 3, FormantRatio = 1.1, RingModHz = 400, RingModMix = 0.5, ChorusMix = 0.6, OutputGainDb = 7,
        },
        new()
        {
            // Medio susurro que tiembla, fino y lejano, con un eco y una reverb larga.
            Id = "fantasma", Name = "Fantasma", Icon = "👻", IsBuiltIn = true,
            WhisperMix = 0.6, VibratoHz = 3.5, VibratoSemitones = 0.6, HighPassHz = 250,
            FlangerMix = 0.3, FlangerHz = 0.12, EchoMs = 320, EchoFeedback = 0.35, EchoMix = 0.25,
            ReverbMix = 0.5, ReverbSize = 0.92, OutputGainDb = 2.5,
        },
        new()
        {
            // El tono se tambalea despacio y sin ritmo fijo, y la voz suena apagada y algo emborronada.
            Id = "borracho", Name = "Borracho", Icon = "🥴", IsBuiltIn = true,
            PitchSemitones = -1, FormantRatio = 0.96, VibratoHz = 0.45, VibratoSemitones = 1.3,
            LowPassHz = 4000, ChorusMix = 0.2, ReverbMix = 0.1, ReverbSize = 0.4, OutputGainDb = 2,
        },
        new()
        {
            Id = "susurro", Name = "Susurro", Icon = "🤫", IsBuiltIn = true,
            WhisperMix = 1, HighPassHz = 150, PresenceDb = 2, OutputGainDb = 0,
        },
        new()
        {
            // Cada trozo de 0,2 s se oye al revés. Invertir obliga a esperar a que acabe el trozo: añade 200 ms.
            Id = "invertida", Name = "Invertida", Icon = "⏪", IsBuiltIn = true,
            ReverseMs = 200, OutputGainDb = 1.5,
        },
        new()
        {
            Id = "agua", Name = "Bajo el agua", Icon = "🌊", IsBuiltIn = true,
            VibratoHz = 7, VibratoSemitones = 0.5, LowPassHz = 650, ChorusMix = 0.35, FlangerMix = 0.25, FlangerHz = 0.6,
            ReverbMix = 0.2, ReverbSize = 0.5, OutputGainDb = 4,
        },
        new()
        {
            Id = "espacial", Name = "Espacial", Icon = "🚀", IsBuiltIn = true,
            HighPassHz = 150, PresenceDb = 2, FlangerMix = 0.55, FlangerHz = 0.15,
            EchoMs = 420, EchoFeedback = 0.45, EchoMix = 0.3, ReverbMix = 0.35, ReverbSize = 0.9, OutputGainDb = 1,
        },
        new()
        {
            Id = "radio", Name = "Radio", Icon = "📻", IsBuiltIn = true,
            HighPassHz = 300, LowPassHz = 3400, PresenceDb = 4, Distortion = 0.35, OutputGainDb = -3.5,
        },
        new()
        {
            Id = "telefono", Name = "Teléfono", Icon = "📞", IsBuiltIn = true,
            HighPassHz = 400, LowPassHz = 3000, BitcrushRateHz = 8000, Distortion = 0.1, PresenceDb = 3, OutputGainDb = -2,
        },
        new()
        {
            Id = "megafono", Name = "Megáfono", Icon = "📢", IsBuiltIn = true,
            HighPassHz = 500, LowPassHz = 4000, PresenceDb = 6, Distortion = 0.6, OutputGainDb = -1,
        },
        new()
        {
            Id = "distorsion", Name = "Distorsión", Icon = "🎸", IsBuiltIn = true,
            Distortion = 0.85, HighPassHz = 150, LowPassHz = 7000, PresenceDb = 3, OutputGainDb = -3.5,
        },
        new()
        {
            // Sin graves ni agudos y con más sala que voz directa, como si hablaras desde el otro lado de una nave.
            Id = "lejana", Name = "Lejana", Icon = "🏔", IsBuiltIn = true,
            HighPassHz = 350, LowPassHz = 3500, PresenceDb = -2, ReverbMix = 0.7, ReverbSize = 0.75, OutputGainDb = -2.5,
        },
        new()
        {
            Id = "cueva", Name = "Cueva / Eco", Icon = "🦇", IsBuiltIn = true,
            EchoMs = 280, EchoFeedback = 0.35, EchoMix = 0.35, ReverbMix = 0.45, ReverbSize = 0.9, LowPassHz = 7000, OutputGainDb = -2.5,
        },
        new()
        {
            Id = "reverb", Name = "Reverb", Icon = "🏛", IsBuiltIn = true,
            ReverbMix = 0.45, ReverbSize = 0.85, OutputGainDb = -1.5,
        },
        new()
        {
            Id = "delay", Name = "Delay", Icon = "🔁", IsBuiltIn = true,
            EchoMs = 160, EchoFeedback = 0.5, EchoMix = 0.45, OutputGainDb = -1,
        },
        new()
        {
            Id = "chorus", Name = "Chorus", Icon = "👥", IsBuiltIn = true,
            ChorusMix = 0.9, OutputGainDb = 3.5,
        },
        new()
        {
            Id = "flanger", Name = "Flanger", Icon = "🌀", IsBuiltIn = true,
            FlangerMix = 0.8, FlangerHz = 0.25, OutputGainDb = 2,
        },
    ];

    public static VoicePreset? Find(string id) =>
        id == VoicePreset.Neutral.Id ? VoicePreset.Neutral : All.FirstOrDefault(v => v.Id == id);
}
