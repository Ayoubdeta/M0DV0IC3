using M0DV0IC3.Dsp;

namespace M0DV0IC3.Cli;

/// <summary>
/// Señal de prueba parecida a la voz, para ejercitar YIN y PSOLA sin micro:
/// <list type="bullet">
/// <item>Fuente: pulsos glotales de Rosenberg, derivados (incluye la radiación de los labios), con algo de aspiración.</item>
/// <item>Entonación: dos ondas lentas y un vibrato de 5,5 Hz, siempre dentro de ±20 % de f0.</item>
/// <item>Tracto vocal: 4 resonadores de Klatt en cascada que se mueven suavemente entre /a e i o u/.</item>
/// <item>Sílabas con oclusivas sordas, pausas entre palabras y fricativas (s, sh, f) de ruido filtrado.</item>
/// </list>
/// Es determinista: con los mismos parámetros sale siempre la misma señal.
/// </summary>
internal static class SpeechSynth
{
    public const int Rate = DspMath.SampleRate;
    public const double MaxPitchDeviation = 0.175;

    // Rosenberg: fracción del periodo en apertura y en cierre (el resto, glotis cerrada).
    private const double OpenPhase = 0.40;
    private const double ClosingPhase = 0.16;
    private const int CoefficientStep = 16;

    // F1-F4 aproximados de las vocales del castellano, voz masculina.
    private static readonly double[][] VowelFormants =
    [
        [750, 1300, 2500, 3500], // a
        [450, 1900, 2500, 3500], // e
        [300, 2250, 2950, 3700], // i
        [500, 950, 2450, 3400],  // o
        [330, 780, 2350, 3300],  // u
    ];

    private static readonly double[] FormantBandwidths = [80, 100, 140, 200];

    // s, sh, f: centro y ancho de banda del ruido.
    private static readonly (double Hz, double Bandwidth)[] Fricatives = [(5500, 2000), (3000, 1200), (4500, 4000)];

    private enum Kind
    {
        Vowel,
        Fricative,
    }

    private readonly record struct Segment(Kind Kind, int Start, int Length, int Type, double Gain);

    /// <summary>f0 instantánea en el segundo <paramref name="t"/>.</summary>
    public static double PitchAt(double f0, double t) =>
        f0 * (1
            + 0.100 * Math.Sin(2 * Math.PI * 0.43 * t)
            + 0.060 * Math.Sin(2 * Math.PI * 1.1 * t + 1.3)
            + 0.015 * Math.Sin(2 * Math.PI * 5.5 * t));

    /// <summary>Genera la señal con pico de -6 dBFS.</summary>
    public static float[] Generate(double seconds, double f0, int seed = 7)
    {
        int length = Math.Max(1, (int)Math.Round(seconds * Rate));
        var random = new Random(seed);
        var segments = Schedule(length, random);

        var voiced = new float[length];
        var unvoiced = new float[length];
        RenderVoiced(voiced, segments, f0, random);
        RenderFricatives(unvoiced, segments, random);

        // Fricativas unos 10 dB por debajo de las vocales, como en el habla.
        double voicedRms = ActiveRms(voiced, segments, Kind.Vowel);
        double unvoicedRms = ActiveRms(unvoiced, segments, Kind.Fricative);
        float fricativeGain = unvoicedRms > 0 ? (float)(0.32 * voicedRms / unvoicedRms) : 0f;

        var output = new float[length];
        for (int i = 0; i < length; i++) output[i] = voiced[i] + fricativeGain * unvoiced[i];

        float peak = DspMath.Peak(output);
        float scale = peak > 0 ? DspMath.DbToGain(-6) / peak : 0f;
        // Ruido de fondo de un micro real, unos -75 dBFS RMS (ruido uniforme: RMS = amplitud / raíz de 3).
        float floor = DspMath.DbToGain(-75) * MathF.Sqrt(3);
        for (int i = 0; i < length; i++) output[i] = output[i] * scale + floor * (float)(random.NextDouble() * 2 - 1);
        return output;
    }

    private static List<Segment> Schedule(int length, Random random)
    {
        var segments = new List<Segment>();
        int pos = Ms(250);
        int word = 0;
        while (pos < length)
        {
            int syllables = random.Next(2, 4);
            int stressed = random.Next(syllables);
            for (int s = 0; s < syllables && pos < length; s++)
            {
                // Entre sílabas, el cierre de una oclusiva sorda (p, t, k).
                if (s > 0) pos += Ms(random, 25, 50);
                if (random.NextDouble() < 0.35)
                {
                    int fricative = Ms(random, 70, 130);
                    Add(Kind.Fricative, pos, fricative, random.Next(Fricatives.Length), 1.0);
                    pos += fricative;
                }
                int vowel = Ms(random, 160, 320);
                double gain = s == stressed ? 1.0 : 0.6 + 0.25 * random.NextDouble();
                Add(Kind.Vowel, pos, vowel, random.Next(VowelFormants.Length), gain);
                pos += vowel;
            }
            word++;
            pos += word % 4 == 0 ? Ms(random, 300, 450) : Ms(random, 90, 200);
        }
        return segments;

        void Add(Kind kind, int start, int count, int type, double gain)
        {
            count = Math.Min(count, length - start);
            if (count > 0) segments.Add(new Segment(kind, start, count, type, gain));
        }
    }

    private static void RenderVoiced(float[] output, List<Segment> segments, double f0, Random random)
    {
        int n = output.Length;
        var envelope = new float[n];
        var vowelAt = new byte[n];

        // La vocal objetivo se mantiene durante las pausas, hasta la siguiente vocal.
        var vowels = segments.Where(s => s.Kind == Kind.Vowel).ToList();
        if (vowels.Count == 0) return;
        Array.Fill(vowelAt, (byte)vowels[0].Type);
        for (int v = 0; v < vowels.Count; v++)
        {
            int end = v + 1 < vowels.Count ? vowels[v + 1].Start : n;
            Array.Fill(vowelAt, (byte)vowels[v].Type, vowels[v].Start, end - vowels[v].Start);
            WriteEnvelope(envelope, vowels[v], attackMs: 30, releaseMs: 45);
        }

        var resonators = FormantBandwidths.Select(_ => new Resonator()).ToArray();
        var formants = (double[])VowelFormants[vowelAt[0]].Clone();
        // Coarticulación: los formantes van hacia la vocal actual con una constante de tiempo de 20 ms.
        double glide = 1 - Math.Exp(-1.0 / (0.020 * Rate));
        double phase = 0, previousFlow = 0;

        for (int i = 0; i < n; i++)
        {
            double increment = PitchAt(f0, (double)i / Rate) / Rate;
            phase += increment;
            if (phase >= 1) phase -= 1;

            // Derivada del flujo glotal por unidad de fase: el nivel no depende de f0.
            double flow = GlottalFlow(phase);
            double excitation = (flow - previousFlow) / increment;
            previousFlow = flow;
            double aspiration = 0.15 * flow * (random.NextDouble() * 2 - 1);
            double x = (excitation + aspiration) * envelope[i];

            var target = VowelFormants[vowelAt[i]];
            for (int k = 0; k < formants.Length; k++) formants[k] += glide * (target[k] - formants[k]);
            if (i % CoefficientStep == 0)
            {
                for (int k = 0; k < resonators.Length; k++) resonators[k].Set(formants[k], FormantBandwidths[k]);
            }
            foreach (var resonator in resonators) x = resonator.Process(x);
            output[i] = (float)x;
        }
    }

    private static void RenderFricatives(float[] output, List<Segment> segments, Random random)
    {
        var resonator = new Resonator();
        var envelope = new float[output.Length];
        foreach (var segment in segments.Where(s => s.Kind == Kind.Fricative))
        {
            WriteEnvelope(envelope, segment, attackMs: 15, releaseMs: 20);
            var (hz, bandwidth) = Fricatives[segment.Type];
            resonator.Set(hz, bandwidth);
            double previous = 0;
            for (int i = segment.Start; i < segment.Start + segment.Length; i++)
            {
                // Ruido blanco con preénfasis (diferencia primera) para quitarle los graves.
                double noise = random.NextDouble() * 2 - 1;
                double x = (noise - previous) * envelope[i];
                previous = noise;
                output[i] = (float)resonator.Process(x);
            }
        }
    }

    /// <summary>Flujo glotal de Rosenberg (tipo B) para una fase 0..1 del periodo.</summary>
    private static double GlottalFlow(double phase)
    {
        if (phase < OpenPhase) return 0.5 * (1 - Math.Cos(Math.PI * phase / OpenPhase));
        if (phase < OpenPhase + ClosingPhase) return Math.Cos(0.5 * Math.PI * (phase - OpenPhase) / ClosingPhase);
        return 0;
    }

    /// <summary>Envolvente del segmento con rampas de coseno alzado al principio y al final.</summary>
    private static void WriteEnvelope(float[] envelope, Segment segment, double attackMs, double releaseMs)
    {
        int attack = Math.Max(1, Math.Min(Ms(attackMs), segment.Length / 2));
        int release = Math.Max(1, Math.Min(Ms(releaseMs), segment.Length / 2));
        for (int i = 0; i < segment.Length; i++)
        {
            double g = 1;
            if (i < attack) g = Square(Math.Sin(0.5 * Math.PI * i / attack));
            int fromEnd = segment.Length - 1 - i;
            if (fromEnd < release) g = Math.Min(g, Square(Math.Sin(0.5 * Math.PI * fromEnd / release)));
            envelope[segment.Start + i] = (float)(g * segment.Gain);
        }
    }

    private static double ActiveRms(float[] x, List<Segment> segments, Kind kind)
    {
        double sum = 0;
        long count = 0;
        foreach (var segment in segments.Where(s => s.Kind == kind))
        {
            for (int i = segment.Start; i < segment.Start + segment.Length; i++) sum += x[i] * (double)x[i];
            count += segment.Length;
        }
        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private static double Square(double x) => x * x;

    private static int Ms(double milliseconds) => (int)Math.Round(milliseconds * Rate / 1000);

    private static int Ms(Random random, double min, double max) => Ms(min + (max - min) * random.NextDouble());

    /// <summary>Resonador de 2 polos de Klatt, con ganancia 1 en continua (en cascada da el espectro de la vocal).</summary>
    private sealed class Resonator
    {
        private double _a, _b, _c, _y1, _y2;

        public void Set(double hz, double bandwidth)
        {
            double r = Math.Exp(-Math.PI * bandwidth / Rate);
            _c = -r * r;
            _b = 2 * r * Math.Cos(2 * Math.PI * hz / Rate);
            _a = 1 - _b - _c;
        }

        public double Process(double x)
        {
            double y = _a * x + _b * _y1 + _c * _y2;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
