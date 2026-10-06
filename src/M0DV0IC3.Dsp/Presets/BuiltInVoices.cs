using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Dsp.Presets;

/// <summary>
/// Voces incluidas de serie: primero personajes y después efectos y ambientes. Los valores son un punto de partida;
/// <c>OutputGainDb</c> está ajustado con <c>m0dv0ic3-cli bench</c> (columna "Nivel") para que todas suenen al
/// mismo volumen que la voz original (±0,5 dB RMS). La excepción es "Lejana", que suena 3 dB más baja a propósito.
/// <para>Las voces de otra edad o de otro sexo tienen <see cref="VoicePreset.TargetPitchHz"/>: llevan tu tono medio a
/// un tono de mujer, de niño… sea tu voz grave o aguda. Con un tono fijo (+5 semitonos), una voz de hombre grave
/// (~100 Hz) se quedaba en ~135 Hz, que sigue sonando a hombre.</para>
/// </summary>
public static class BuiltInVoices
{
    public static IReadOnlyList<VoicePreset> All { get; } =
    [
        new()
        {
            Id = "grave", Name = "Grave", Icon = "🐻", IsBuiltIn = true,
            PitchSemitones = -5, TargetPitchHz = 82, FormantRatio = 0.85, HighShelfDb = -2, OutputGainDb = 2.5,
        },
        new()
        {
            Id = "hombre", Name = "Hombre", Icon = "🧔", IsBuiltIn = true,
            PitchSemitones = -4, TargetPitchHz = 110, FormantRatio = 0.88, OutputGainDb = 1.5,
        },
        new()
        {
            // Tono de mujer adulta (~215 Hz) desde cualquier voz y tracto vocal un 20 % más corto.
            Id = "mujer", Name = "Mujer", Icon = "👩", IsBuiltIn = true,
            PitchSemitones = 5, TargetPitchHz = 215, FormantRatio = 1.2, Breathiness = 0.12, HighPassHz = 120, HighShelfDb = 2, OutputGainDb = 1.5,
        },
        new()
        {
            // Muy femenina: más aguda que "Mujer" (~245 Hz), tracto vocal más corto sin llegar a sonar infantil,
            // menos pecho, más brillo y voz con aire.
            Id = "mujer2", Name = "Mujer 2", Icon = "💃", IsBuiltIn = true,
            PitchSemitones = 8, TargetPitchHz = 245, FormantRatio = 1.22, Breathiness = 0.35,
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
            PitchSemitones = 8, TargetPitchHz = 230, FormantRatio = 1.2, Breathiness = 0.2,
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
            PitchSemitones = 6, TargetPitchHz = 290, FormantRatio = 1.25, HighPassHz = 150, HighShelfDb = 1.5, OutputGainDb = 1,
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
            PitchSemitones = -8, TargetPitchHz = 58, FormantRatio = 0.75, Distortion = 0.25, LowPassHz = 6000,
            ReverbMix = 0.25, ReverbSize = 0.75, OutputGainDb = -2,
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
            // Muy aguda (~420 Hz) y con una boca y una garganta diminutas, con un temblor ligero.
            Id = "bebe", Name = "Bebé", Icon = "👶", IsBuiltIn = true,
            PitchSemitones = 12, TargetPitchHz = 420, FormantRatio = 1.55, VibratoHz = 6.5, VibratoSemitones = 0.35,
            HighPassHz = 250, HighShelfDb = 1, OutputGainDb = 0,
        },
        new()
        {
            // Voz temblorosa: vibrato irregular, el volumen tiembla (modulador a 5,5 Hz) y algo de aire y ronquera.
            Id = "abuelo", Name = "Abuelo", Icon = "👴", IsBuiltIn = true,
            TargetPitchHz = 125, FormantRatio = 0.95, VibratoHz = 6, VibratoSemitones = 0.45,
            RingModHz = 5.5, RingModMix = 0.18, Breathiness = 0.15, Distortion = 0.06,
            HighPassHz = 130, LowPassHz = 6500, OutputGainDb = -1.5,
        },
        new()
        {
            Id = "abuela", Name = "Abuela", Icon = "👵", IsBuiltIn = true,
            PitchSemitones = 5, TargetPitchHz = 200, FormantRatio = 1.12, VibratoHz = 6.5, VibratoSemitones = 0.5,
            RingModHz = 6, RingModMix = 0.18, Breathiness = 0.2, HighPassHz = 160, LowPassHz = 7000, OutputGainDb = 3.5,
        },
        new()
        {
            // Enorme: muy grave (~55 Hz), garganta un tercio más grande, graves realzados y una sala gigante.
            Id = "gigante", Name = "Gigante", Icon = "👹", IsBuiltIn = true,
            PitchSemitones = -10, TargetPitchHz = 55, FormantRatio = 0.68, LowShelfDb = 3, LowPassHz = 5000,
            Compression = 0.4, ReverbMix = 0.35, ReverbSize = 0.95, OutputGainDb = -1.5,
        },
        new()
        {
            // Pequeñito y nasal: aguda, formantes muy altos y una campana estrecha en 1,1 kHz.
            Id = "duende", Name = "Duende", Icon = "🧝", IsBuiltIn = true,
            PitchSemitones = 9, TargetPitchHz = 290, FormantRatio = 1.45, NasalDb = 9, HighPassHz = 300, HighShelfDb = 1,
            OutputGainDb = -2,
        },
        new()
        {
            // Aguda y dulce (~330 Hz), con aire y muy brillante.
            Id = "anime", Name = "Chica anime", Icon = "🌸", IsBuiltIn = true,
            PitchSemitones = 10, TargetPitchHz = 330, FormantRatio = 1.28, Breathiness = 0.15,
            HighPassHz = 200, PresenceDb = 2, HighShelfDb = 4, OutputGainDb = -0.5,
        },
        new()
        {
            // Voz de anuncio: un poco más grave, mucho pecho (graves +5 dB), comprimida y con presencia.
            Id = "locutor", Name = "Locutor de radio", Icon = "🗣", IsBuiltIn = true,
            PitchSemitones = -2, TargetPitchHz = 90, FormantRatio = 0.94, LowShelfDb = 5, Compression = 0.8,
            PresenceDb = 2.5, HighShelfDb = 1.5, Distortion = 0.08, HighPassHz = 60, OutputGainDb = 2.5,
        },
        new()
        {
            // Grave y metálica, como dentro de un casco: un comb de 2,6 ms hace de máscara.
            Id = "villano", Name = "Villano espacial", Icon = "🦹", IsBuiltIn = true,
            PitchSemitones = -7, TargetPitchHz = 72, FormantRatio = 0.8, Breathiness = 0.1,
            CombMs = 2.6, CombFeedback = 0.55, CombMix = 0.35, Distortion = 0.15, Compression = 0.6,
            LowShelfDb = 3, LowPassHz = 6500, ReverbMix = 0.12, ReverbSize = 0.4, OutputGainDb = 1,
        },
        new()
        {
            // Grave, ronca y rasgada: el modulador a 38 Hz hace el gruñido y la distorsión, la voz rota.
            Id = "zombi", Name = "Zombi", Icon = "🧟", IsBuiltIn = true,
            PitchSemitones = -6, TargetPitchHz = 80, FormantRatio = 0.85, VibratoHz = 0.8, VibratoSemitones = 0.4,
            RingModHz = 38, RingModMix = 0.35, Distortion = 0.4, HighPassHz = 90, LowPassHz = 3500, OutputGainDb = -1,
        },
        new()
        {
            // Pato: casi el mismo tono pero una garganta diminuta y muy nasal.
            Id = "pato", Name = "Pato", Icon = "🦆", IsBuiltIn = true,
            PitchSemitones = 3, FormantRatio = 1.65, NasalDb = 10, HighPassHz = 300, OutputGainDb = -1.5,
        },
        new()
        {
            // Hada: muy aguda (~380 Hz), con aire, un brillo de coro y sala grande.
            Id = "hada", Name = "Hada", Icon = "🧚", IsBuiltIn = true,
            PitchSemitones = 12, TargetPitchHz = 380, FormantRatio = 1.35, Breathiness = 0.3, HighPassHz = 250,
            HighShelfDb = 4, ChorusMix = 0.25, ReverbMix = 0.3, ReverbSize = 0.85, OutputGainDb = 1.5,
        },
        new()
        {
            // Bruja: voz de mujer mayor, nasal y temblorosa, algo rota.
            Id = "bruja", Name = "Bruja", Icon = "🧹", IsBuiltIn = true,
            PitchSemitones = 6, TargetPitchHz = 230, FormantRatio = 1.1, NasalDb = 7, VibratoHz = 5.5, VibratoSemitones = 0.5,
            Distortion = 0.1, HighPassHz = 180, ReverbMix = 0.15, ReverbSize = 0.6, OutputGainDb = -3.5,
        },
        new()
        {
            // Ogro: muy grave, garganta enorme, nasal y bruto.
            Id = "ogro", Name = "Ogro", Icon = "👺", IsBuiltIn = true,
            PitchSemitones = -8, TargetPitchHz = 70, FormantRatio = 0.72, NasalDb = 5, LowShelfDb = 3, Distortion = 0.25,
            LowPassHz = 5000, OutputGainDb = -3.5,
        },
        new()
        {
            // Vampiro: grave y susurrante, con una sala de castillo.
            Id = "vampiro", Name = "Vampiro", Icon = "🧛", IsBuiltIn = true,
            PitchSemitones = -3, TargetPitchHz = 85, FormantRatio = 0.88, Breathiness = 0.18, PresenceDb = -1,
            EchoMs = 380, EchoFeedback = 0.2, EchoMix = 0.12, ReverbMix = 0.3, ReverbSize = 0.85, OutputGainDb = 2.5,
        },
        new()
        {
            // Poseído: tu voz con dos voces demoníacas por debajo (una octava, una cuarta y una octava y quinta).
            Id = "poseido", Name = "Poseído", Icon = "💀", IsBuiltIn = true,
            HarmonyMix = 0.9, Harmony = HarmonyStyle.Demonic, Distortion = 0.2, LowPassHz = 6000,
            ReverbMix = 0.3, ReverbSize = 0.8, OutputGainDb = -5,
        },
        new()
        {
            // Voz divina: grave y solemne, con octavas arriba y abajo y una catedral.
            Id = "divina", Name = "Voz divina", Icon = "👼", IsBuiltIn = true,
            PitchSemitones = -3, TargetPitchHz = 95, FormantRatio = 0.92, HarmonyMix = 0.6, Harmony = HarmonyStyle.Angelic,
            EchoMs = 450, EchoFeedback = 0.3, EchoMix = 0.15, ReverbMix = 0.5, ReverbSize = 0.97, OutputGainDb = -1,
        },
        new()
        {
            // Insecto: muy pequeño y zumbón (modulador a 180 Hz).
            Id = "insecto", Name = "Insecto", Icon = "🐝", IsBuiltIn = true,
            PitchSemitones = 10, FormantRatio = 1.6, RingModHz = 180, RingModMix = 0.4, HighPassHz = 400, OutputGainDb = 6,
        },
        new()
        {
            // Cíborg: tu voz con un toque metálico y digital, sin perder lo humano.
            Id = "ciborg", Name = "Cíborg", Icon = "🦾", IsBuiltIn = true,
            FormantRatio = 0.95, RingModHz = 90, RingModMix = 0.25, CombMs = 3.5, CombFeedback = 0.45, CombMix = 0.3,
            BitcrushRateHz = 16000, BitcrushBits = 8, Compression = 0.5, OutputGainDb = 1,
        },
        new()
        {
            // Robot gigante: grave, metálico y saturado, en un hangar.
            Id = "mecha", Name = "Robot gigante", Icon = "⚙", IsBuiltIn = true,
            PitchSemitones = -8, TargetPitchHz = 62, FormantRatio = 0.8, RingModHz = 50, RingModMix = 0.3,
            CombMs = 5, CombFeedback = 0.6, CombMix = 0.4, Distortion = 0.3, ReverbMix = 0.2, ReverbSize = 0.7, OutputGainDb = 1,
        },
        new()
        {
            // Androide: afinación perfecta e instantánea (autotune cromático) y un brillo metálico: una voz de IA.
            Id = "androide", Name = "Androide", Icon = "💻", IsBuiltIn = true,
            FormantRatio = 1.03, AutotuneScale = AutotuneScale.Chromatic, AutotuneRetuneMs = 0,
            CombMs = 2, CombFeedback = 0.3, CombMix = 0.2, PresenceDb = 2, Compression = 0.4, OutputGainDb = -1,
        },
        new()
        {
            // Payaso: agudo, nasal y con un vibrato rápido y tonto.
            Id = "payaso", Name = "Payaso", Icon = "🤡", IsBuiltIn = true,
            PitchSemitones = 7, TargetPitchHz = 260, FormantRatio = 1.3, NasalDb = 8, VibratoHz = 7, VibratoSemitones = 0.6,
            ChorusMix = 0.2, HighPassHz = 200, OutputGainDb = 0,
        },
        new()
        {
            // Hombre lobo: grave, con gruñido (modulador a 30 Hz), aire y rasgado.
            Id = "lobo", Name = "Hombre lobo", Icon = "🐺", IsBuiltIn = true,
            PitchSemitones = -6, TargetPitchHz = 75, FormantRatio = 0.8, RingModHz = 30, RingModMix = 0.25, Breathiness = 0.2,
            Distortion = 0.3, LowPassHz = 5000, ReverbMix = 0.15, ReverbSize = 0.6, OutputGainDb = -1,
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
            // Tu voz con copias una octava abajo, una quinta y una octava arriba, en una sala grande.
            Id = "coro", Name = "Coro", Icon = "🎵", IsBuiltIn = true,
            HarmonyMix = 0.8, ChorusMix = 0.3, ReverbMix = 0.25, ReverbSize = 0.8, OutputGainDb = 2,
        },
        new()
        {
            // El robot musical de la música electrónica: tu voz toca un acorde de La menor.
            Id = "vocoder", Name = "Vocoder", Icon = "🎹", IsBuiltIn = true,
            VocoderMix = 1, VocoderHz = 110, HighPassHz = 80, Compression = 0.3, OutputGainDb = -2.5,
        },
        new()
        {
            // Radio militar: banda estrecha, saturada, con ruido mientras hablas, chasquido al empezar y pitido al acabar.
            Id = "walkie", Name = "Walkie-talkie", Icon = "📟", IsBuiltIn = true,
            HighPassHz = 500, LowPassHz = 2800, Distortion = 0.45, PresenceDb = 3, Compression = 0.5,
            Transmission = TransmissionStyle.Walkie, TransmissionNoise = 0.7, OutputGainDb = 0,
        },
        new()
        {
            // Dentro de un casco: apagada y con resonancia metálica, y los pitidos de la NASA al empezar y al acabar.
            Id = "astronauta", Name = "Astronauta", Icon = "🛰", IsBuiltIn = true,
            HighPassHz = 300, LowPassHz = 3600, CombMs = 1.6, CombFeedback = 0.4, CombMix = 0.3, PresenceDb = 2,
            Distortion = 0.15, ReverbMix = 0.12, ReverbSize = 0.3, Transmission = TransmissionStyle.Space,
            TransmissionNoise = 0.5, OutputGainDb = -2,
        },
        new()
        {
            // Videojuego de 8 bits: afinación perfecta, 6 bits y 6 kHz.
            Id = "8bits", Name = "8 bits", Icon = "👾", IsBuiltIn = true,
            AutotuneScale = AutotuneScale.Chromatic, AutotuneRetuneMs = 0, BitcrushRateHz = 6000, BitcrushBits = 6,
            HighPassHz = 200, OutputGainDb = 1,
        },
        new()
        {
            // Disco antiguo: banda estrecha, saturado y con el «wow» lento de un tocadiscos.
            Id = "vinilo", Name = "Disco antiguo", Icon = "💿", IsBuiltIn = true,
            VibratoHz = 0.6, VibratoSemitones = 0.15, HighPassHz = 400, LowPassHz = 3200, Distortion = 0.25,
            BitcrushRateHz = 11000, ReverbMix = 0.1, ReverbSize = 0.3, OutputGainDb = -1,
        },
        new()
        {
            // Dentro de una lata: resonancia metálica muy corta.
            Id = "lata", Name = "Dentro de una lata", Icon = "🥫", IsBuiltIn = true,
            CombMs = 1.2, CombFeedback = 0.6, CombMix = 0.45, HighPassHz = 400, LowPassHz = 4500, OutputGainDb = 6,
        },
        new()
        {
            // Con mascarilla: sin agudos y apagada.
            Id = "mascarilla", Name = "Con mascarilla", Icon = "😷", IsBuiltIn = true,
            LowPassHz = 1800, PresenceDb = -4, HighShelfDb = -6, OutputGainDb = 0.5,
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
        new()
        {
            // El locutor de un estadio: comprimido, con el eco del graderío y mucha sala.
            Id = "estadio", Name = "Estadio", Icon = "🏟", IsBuiltIn = true,
            Compression = 0.5, PresenceDb = 2, EchoMs = 380, EchoFeedback = 0.35, EchoMix = 0.3,
            ReverbMix = 0.45, ReverbSize = 0.95, OutputGainDb = -4,
        },
        new()
        {
            // Ducha: sala pequeña y brillante.
            Id = "ducha", Name = "Ducha", Icon = "🚿", IsBuiltIn = true,
            HighShelfDb = 2, ReverbMix = 0.35, ReverbSize = 0.25, OutputGainDb = 1,
        },
        new()
        {
            Id = "catedral", Name = "Catedral", Icon = "⛪", IsBuiltIn = true,
            LowPassHz = 8000, ReverbMix = 0.6, ReverbSize = 0.98, OutputGainDb = -6,
        },
        new()
        {
            // Megafonía de una estación: altavoz de banda estrecha en una nave grande.
            Id = "megafonia", Name = "Megafonía", Icon = "🚉", IsBuiltIn = true,
            HighPassHz = 300, LowPassHz = 4000, PresenceDb = 4, Distortion = 0.15, EchoMs = 350, EchoFeedback = 0.25,
            EchoMix = 0.2, ReverbMix = 0.35, ReverbSize = 0.9, OutputGainDb = -4.5,
        },
    ];

    public static VoicePreset? Find(string id) =>
        id == VoicePreset.Neutral.Id ? VoicePreset.Neutral : All.FirstOrDefault(v => v.Id == id);
}
