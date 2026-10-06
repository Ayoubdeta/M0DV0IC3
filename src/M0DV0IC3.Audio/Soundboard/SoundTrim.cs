using M0DV0IC3.Dsp;

namespace M0DV0IC3.Audio.Soundboard;

/// <summary>
/// Recorte de un sonido del soundboard sin tocar su archivo: se guardan el inicio y el fin, y al cargarlo se copia
/// solo ese trozo, con 5 ms de fundido en cada corte para que no haga clic.
/// </summary>
public static class SoundTrim
{
    /// <summary>Lo más corto que puede quedar un sonido recortado.</summary>
    public const double MinimumSeconds = 0.05;

    private const double FadeSeconds = 0.005;

    /// <param name="endSeconds">Fin del trozo; 0 o más que la duración = hasta el final.</param>
    public static SoundClip Apply(SoundClip full, double startSeconds, double endSeconds)
    {
        int length = full.Samples.Length;
        int start = Math.Clamp((int)Math.Round(startSeconds * DspMath.SampleRate), 0, length);
        int end = endSeconds > 0 ? Math.Clamp((int)Math.Round(endSeconds * DspMath.SampleRate), start, length) : length;
        if (start == 0 && end == length) return full;

        var samples = full.Samples.AsSpan(start, end - start).ToArray();
        int fade = Math.Min((int)(FadeSeconds * DspMath.SampleRate), samples.Length / 2);
        for (int i = 0; i < fade; i++)
        {
            float gain = (float)i / fade;
            if (start > 0) samples[i] *= gain;
            if (end < length) samples[^(i + 1)] *= gain;
        }
        return new SoundClip(full.Id, full.Name, samples);
    }

    /// <summary>
    /// Dónde empieza y acaba el sonido de verdad: los silencios del principio y del final (por debajo de
    /// <paramref name="thresholdDb"/> respecto al pico) se quitan, dejando 20 ms de margen.
    /// </summary>
    public static (double Start, double End) DetectSound(float[] samples, double thresholdDb = -40)
    {
        if (samples.Length == 0) return (0, 0);
        float threshold = DspMath.Peak(samples) * DspMath.DbToGain(thresholdDb);
        int first = Array.FindIndex(samples, x => Math.Abs(x) > threshold);
        int last = Array.FindLastIndex(samples, x => Math.Abs(x) > threshold);
        if (first < 0) return (0, (double)samples.Length / DspMath.SampleRate);

        int margin = DspMath.SampleRate / 50;
        double start = Math.Max(0, first - margin) / (double)DspMath.SampleRate;
        double end = Math.Min(samples.Length, last + 1 + margin) / (double)DspMath.SampleRate;
        return (start, end);
    }

    /// <summary>Pico de cada uno de <paramref name="buckets"/> trozos iguales, para dibujar la forma de onda.</summary>
    public static float[] Peaks(float[] samples, int buckets)
    {
        var peaks = new float[buckets];
        if (samples.Length == 0) return peaks;
        for (int b = 0; b < buckets; b++)
        {
            int from = (int)((long)samples.Length * b / buckets);
            int to = Math.Max(from + 1, (int)((long)samples.Length * (b + 1) / buckets));
            peaks[b] = DspMath.Peak(samples.AsSpan(from, Math.Min(to, samples.Length) - from));
        }
        return peaks;
    }
}
