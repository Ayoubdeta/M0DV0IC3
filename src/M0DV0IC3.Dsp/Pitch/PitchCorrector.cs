namespace M0DV0IC3.Dsp.Pitch;

/// <summary>Escala a la que el autotune ajusta la voz. <see cref="Off"/> lo apaga.</summary>
public enum AutotuneScale
{
    Off,
    Chromatic,
    Major,
    Minor,
    HarmonicMinor,
    MinorPentatonic,
}

public static class AutotuneNames
{
    public static IReadOnlyList<string> Notes { get; } = ["Do", "Do♯", "Re", "Mi♭", "Mi", "Fa", "Fa♯", "Sol", "La♭", "La", "Si♭", "Si"];

    public static string Scale(AutotuneScale scale) => scale switch
    {
        AutotuneScale.Off => "apagado",
        AutotuneScale.Chromatic => "cromática",
        AutotuneScale.Major => "mayor",
        AutotuneScale.Minor => "menor",
        AutotuneScale.HarmonicMinor => "menor armónica",
        AutotuneScale.MinorPentatonic => "pentatónica menor",
        _ => scale.ToString(),
    };

    /// <summary>"La menor", "cromática"...</summary>
    public static string Describe(AutotuneScale scale, int key) =>
        scale is AutotuneScale.Off or AutotuneScale.Chromatic ? Scale(scale) : $"{Notes[Mod12(key)]} {Scale(scale)}";

    internal static int Mod12(int value) => ((value % 12) + 12) % 12;
}

/// <summary>
/// Autotune: decide en cada grano de PSOLA a qué nota de la escala hay que llevar la voz.
/// <list type="bullet">
/// <item>Con <c>retuneMs</c> = 0 la corrección es instantánea: es el autotune "duro" de la música urbana. La voz
/// salta de nota en nota sin pasar por las intermedias y el vibrato natural desaparece.</item>
/// <item>Con valores mayores, la voz llega a la nota poco a poco y conserva parte de sus inflexiones, así que suena
/// natural.</item>
/// <item>"Exagerar" amplía las subidas y bajadas de la voz respecto de su tono medio reciente (hasta 2,5×) antes de
/// elegir la nota. Al hablar, la voz recorre así muchas más notas y suena cantada, como en las canciones.</item>
/// <item>La histéresis es pequeña (0,1 semitonos): solo evita saltos por el ruido de la medida. Cuando la voz cruza
/// de verdad la frontera entre dos notas, el salto se oye, y ese "quiebro" es parte del sonido del autotune.</item>
/// <item>Como PSOLA no toca los formantes, la voz sigue siendo la tuya, sin efecto ardilla.</item>
/// </list>
/// </summary>
public sealed class PitchCorrector
{
    private const double Hysteresis = 0.1;
    private const double MaxMelodyGain = 2.5;
    private const double CenterSeconds = 1.2;

    // Más de 5 semitonos de golpe respecto del tono medio no es entonación sino un error de octava de la medida:
    // no se amplía más allá.
    private const double MaxDeviation = 5;

    // Bit i = la nota (tónica + i semitonos) está en la escala.
    private const int ChromaticMask = 0b1111_1111_1111;
    private const int MajorMask = 0b1010_1011_0101;           // 0 2 4 5 7 9 11
    private const int MinorMask = 0b0101_1010_1101;           // 0 2 3 5 7 8 10
    private const int HarmonicMinorMask = 0b1001_1010_1101;   // 0 2 3 5 7 8 11
    private const int MinorPentatonicMask = 0b0100_1010_1001; // 0 3 5 7 10

    private readonly int _sampleRate;
    private readonly double _centerSamples;
    private int _mask;
    private double _retuneSamples;
    private double _melodyGain = 1;
    private int _note;
    private bool _hasNote;
    private double _correction;
    private double _center;
    private bool _hasCenter;

    public PitchCorrector(int sampleRate)
    {
        _sampleRate = sampleRate;
        _centerSamples = CenterSeconds * sampleRate;
    }

    public bool IsActive => _mask != 0;

    /// <summary>Nota MIDI (60 = Do central) a la que se está llevando la voz, o -1 si ahora no hay voz.</summary>
    public int CurrentNote => _hasNote ? _note : -1;

    /// <param name="exaggeration">0..1: cuánto se amplía la melodía antes de afinar (0 = nada, 1 = 2,5×).</param>
    public void Configure(AutotuneScale scale, int key, double retuneMs, double exaggeration = 0)
    {
        int intervals = scale switch
        {
            AutotuneScale.Chromatic => ChromaticMask,
            AutotuneScale.Major => MajorMask,
            AutotuneScale.Minor => MinorMask,
            AutotuneScale.HarmonicMinor => HarmonicMinorMask,
            AutotuneScale.MinorPentatonic => MinorPentatonicMask,
            _ => 0,
        };
        int k = AutotuneNames.Mod12(key);
        _mask = ((intervals << k) | (intervals >> (12 - k))) & ChromaticMask;
        _retuneSamples = Math.Clamp(retuneMs, 0, 1000) * _sampleRate / 1000.0;
        _melodyGain = 1 + (MaxMelodyGain - 1) * Math.Clamp(exaggeration, 0, 1);
    }

    /// <summary>Tramo sordo o silencio: la próxima nota se elige sin histéresis ni transición.</summary>
    public void Release() => _hasNote = false;

    /// <summary>
    /// Razón de tono para el siguiente grano sonoro. <paramref name="transposeSemitones"/> desplaza la voz antes de
    /// elegir la nota, para poder afinar también una voz de mujer o de hombre. <paramref name="elapsedSamples"/> es el
    /// tiempo desde el grano anterior (para la velocidad de corrección).
    /// </summary>
    public double NextRatio(double inputHz, double transposeSemitones, double elapsedSamples)
    {
        double input = 69 + 12 * Math.Log2(inputHz / 440.0);
        double melody = input;
        if (_melodyGain > 1)
        {
            // El tono medio sigue a la voz con ~1,2 s de retraso: la melodía se amplía alrededor de él.
            if (!_hasCenter)
            {
                _center = input;
                _hasCenter = true;
            }
            else
            {
                _center += (input - _center) * (1 - Math.Exp(-elapsedSamples / _centerSamples));
            }
            melody = _center + Math.Clamp(input - _center, -MaxDeviation, MaxDeviation) * _melodyGain;
        }

        double wanted = melody + transposeSemitones;
        int nearest = NearestNote(wanted);
        bool first = !_hasNote;
        if (first || !IsAllowed(_note) || Math.Abs(wanted - _note) - Math.Abs(wanted - nearest) > Hysteresis)
            _note = nearest;
        _hasNote = true;

        double target = _note - input;
        if (_retuneSamples <= 0) _correction = target;
        else if (first) _correction = transposeSemitones; // al empezar a cantar se entra en la nota, como un cantante
        else _correction += (target - _correction) * (1 - Math.Exp(-elapsedSamples / _retuneSamples));
        return Math.Pow(2, _correction / 12);
    }

    public void Reset()
    {
        _hasNote = false;
        _hasCenter = false;
        _correction = 0;
    }

    private int NearestNote(double midi)
    {
        int center = (int)Math.Round(midi);
        int best = center;
        double bestDistance = double.MaxValue;
        for (int n = center - 6; n <= center + 6; n++)
        {
            if (!IsAllowed(n)) continue;
            double distance = Math.Abs(midi - n);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = n;
            }
        }
        return best;
    }

    private bool IsAllowed(int note) => ((_mask >> AutotuneNames.Mod12(note)) & 1) != 0;
}
