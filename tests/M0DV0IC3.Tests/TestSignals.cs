using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Filters;

namespace M0DV0IC3.Tests;

/// <summary>Señales sintéticas y análisis independientes del código que se prueba.</summary>
internal static class TestSignals
{
    public const int Rate = DspMath.SampleRate;

    public static float[] Sine(double hz, double seconds, float amplitude = 0.5f)
    {
        var x = new float[(int)(seconds * Rate)];
        for (int i = 0; i < x.Length; i++) x[i] = amplitude * (float)Math.Sin(2 * Math.PI * hz * i / Rate);
        return x;
    }

    public static float[] Sawtooth(double hz, double seconds, float amplitude = 0.5f) => Glide(hz, hz, seconds, amplitude);

    /// <summary>Diente de sierra con un barrido lineal de tono.</summary>
    public static float[] Glide(double fromHz, double toHz, double seconds, float amplitude = 0.5f)
    {
        var x = new float[(int)(seconds * Rate)];
        double phase = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double hz = fromHz + (toHz - fromHz) * i / x.Length;
            phase += hz / Rate;
            phase -= Math.Floor(phase);
            x[i] = amplitude * (float)(2 * phase - 1);
        }
        return x;
    }

    /// <summary>Tren de pulsos filtrado por formantes: se parece más a una vocal que una sierra.</summary>
    public static float[] Vowel(double f0, double seconds, float amplitude = 0.4f, double vibratoHz = 0) =>
        Vowel(t => f0 * (1 + (vibratoHz > 0 ? 0.03 * Math.Sin(2 * Math.PI * vibratoHz * t) : 0)), seconds, amplitude);

    /// <summary>Vocal cuyo tono en cada instante (segundos → Hz) da <paramref name="f0At"/>.</summary>
    public static float[] Vowel(Func<double, double> f0At, double seconds, float amplitude = 0.4f)
    {
        var pulses = new float[(int)(seconds * Rate)];
        double phase = 0;
        for (int i = 0; i < pulses.Length; i++)
        {
            double hz = f0At((double)i / Rate);
            phase += hz / Rate;
            if (phase >= 1)
            {
                phase -= 1;
                pulses[i] = 1f;
            }
        }

        var output = new float[pulses.Length];
        foreach (var (freq, q) in new[] { (700.0, 8.0), (1220.0, 10.0), (2600.0, 12.0) })
        {
            var band = (float[])pulses.Clone();
            var filter = new Biquad();
            filter.SetBandPass(Rate, freq, q);
            filter.Process(band);
            for (int i = 0; i < output.Length; i++) output[i] += band[i];
        }
        Normalize(output, amplitude);
        return output;
    }

    public static float[] Noise(double seconds, float amplitude, int seed = 1)
    {
        var random = new Random(seed);
        var x = new float[(int)(seconds * Rate)];
        for (int i = 0; i < x.Length; i++) x[i] = amplitude * (float)(random.NextDouble() * 2 - 1);
        return x;
    }

    public static void Normalize(float[] x, float peak)
    {
        float max = DspMath.Peak(x);
        if (max <= 0) return;
        for (int i = 0; i < x.Length; i++) x[i] *= peak / max;
    }

    /// <summary>Procesa en bloques del tamaño que entregaría WASAPI.</summary>
    public static float[] ProcessInBlocks(IAudioEffect effect, float[] input, int block = 480)
    {
        var output = (float[])input.Clone();
        for (int offset = 0; offset < output.Length; offset += block)
            effect.Process(output.AsSpan(offset, Math.Min(block, output.Length - offset)));
        return output;
    }

    public static ReadOnlySpan<float> Segment(float[] x, double fromSeconds, double toSeconds) =>
        x.AsSpan((int)(fromSeconds * Rate), (int)((toSeconds - fromSeconds) * Rate));

    /// <summary>
    /// f0 por autocorrelación normalizada (independiente de YIN): toma el primer pico que llega al
    /// 85 % del máximo, para no caer en errores de octava.
    /// </summary>
    public static double MeasureF0(ReadOnlySpan<float> x, double minHz = 50, double maxHz = 1000)
    {
        int minLag = (int)(Rate / maxHz);
        int maxLag = (int)(Rate / minHz);
        int n = x.Length - maxLag - 1;
        if (n <= 0) throw new ArgumentException("Segmento demasiado corto");

        var r = new double[maxLag + 2];
        double energy0 = 0;
        for (int i = 0; i < n; i++) energy0 += x[i] * (double)x[i];
        for (int lag = minLag; lag <= maxLag + 1; lag++)
        {
            double sum = 0, energy = 0;
            for (int i = 0; i < n; i++)
            {
                sum += x[i] * (double)x[i + lag];
                energy += x[i + lag] * (double)x[i + lag];
            }
            r[lag] = sum / Math.Sqrt(energy0 * energy + 1e-20);
        }

        double best = 0;
        for (int lag = minLag + 1; lag <= maxLag; lag++) best = Math.Max(best, r[lag]);
        for (int lag = minLag + 1; lag <= maxLag; lag++)
        {
            if (r[lag] >= 0.85 * best && r[lag] >= r[lag - 1] && r[lag] >= r[lag + 1])
            {
                double denominator = r[lag - 1] - 2 * r[lag] + r[lag + 1];
                double refined = Math.Abs(denominator) > 1e-12 ? lag + 0.5 * (r[lag - 1] - r[lag + 1]) / denominator : lag;
                return Rate / refined;
            }
        }
        return double.NaN;
    }

    /// <summary>Autocorrelación normalizada en un desfase: ~1 para una señal periódica con ese periodo, ~0 para ruido.</summary>
    public static double Periodicity(ReadOnlySpan<float> x, int lag)
    {
        double sum = 0, energyA = 0, energyB = 0;
        for (int i = 0; i + lag < x.Length; i++)
        {
            sum += x[i] * (double)x[i + lag];
            energyA += x[i] * (double)x[i];
            energyB += x[i + lag] * (double)x[i + lag];
        }
        return sum / Math.Sqrt(energyA * energyB + 1e-20);
    }

    /// <summary>Centroide espectral (Hz) con una FFT radix-2 sencilla y ventana Hann.</summary>
    public static double SpectralCentroid(ReadOnlySpan<float> x, int size = 8192)
    {
        double weighted = 0, total = 0;
        int frames = 0;
        for (int start = 0; start + size <= x.Length; start += size / 2, frames++)
        {
            var re = new double[size];
            var im = new double[size];
            for (int i = 0; i < size; i++) re[i] = x[start + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size));
            Fft(re, im);
            for (int k = 1; k < size / 2; k++)
            {
                double magnitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                weighted += magnitude * k * Rate / size;
                total += magnitude;
            }
        }
        return frames == 0 ? double.NaN : weighted / total;
    }

    /// <summary>Desfase (en muestras) que maximiza la correlación cruzada de b respecto de a.</summary>
    public static int BestLag(ReadOnlySpan<float> a, ReadOnlySpan<float> b, int maxLag)
    {
        int bestLag = 0;
        double best = double.MinValue;
        int n = Math.Min(a.Length, b.Length) - maxLag;
        for (int lag = 0; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += a[i] * (double)b[i + lag];
            if (sum > best)
            {
                best = sum;
                bestLag = lag;
            }
        }
        return bestLag;
    }

    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            for (int i = 0; i < n; i += length)
            {
                for (int k = 0; k < length / 2; k++)
                {
                    double wr = Math.Cos(angle * k), wi = Math.Sin(angle * k);
                    int a = i + k, b = i + k + length / 2;
                    double tr = re[b] * wr - im[b] * wi;
                    double ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                }
            }
        }
    }
}
