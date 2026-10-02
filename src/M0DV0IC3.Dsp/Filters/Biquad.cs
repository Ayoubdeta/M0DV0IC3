namespace M0DV0IC3.Dsp.Filters;

/// <summary>
/// Filtro biquad en forma directa II transpuesta, con estado en doble precisión.
/// Coeficientes según el "Audio EQ Cookbook" de Robert Bristow-Johnson.
/// </summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    public bool IsBypassed { get; private set; } = true;

    public void SetBypass()
    {
        _b0 = 1; _b1 = _b2 = _a1 = _a2 = 0;
        IsBypassed = true;
    }

    public void SetLowPass(int sampleRate, double frequency, double q = 0.7071)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        SetNormalized((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void SetHighPass(int sampleRate, double frequency, double q = 0.7071)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        SetNormalized((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>Paso banda con ganancia 0 dB en la frecuencia central.</summary>
    public void SetBandPass(int sampleRate, double frequency, double q)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        SetNormalized(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void SetPeaking(int sampleRate, double frequency, double q, double gainDb)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        double a = Math.Pow(10, gainDb / 40);
        SetNormalized(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    public void SetLowShelf(int sampleRate, double frequency, double gainDb)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, 0.7071);
        double a = Math.Pow(10, gainDb / 40);
        double s = 2 * Math.Sqrt(a) * alpha;
        SetNormalized(
            a * ((a + 1) - (a - 1) * cos + s),
            2 * a * ((a - 1) - (a + 1) * cos),
            a * ((a + 1) - (a - 1) * cos - s),
            (a + 1) + (a - 1) * cos + s,
            -2 * ((a - 1) + (a + 1) * cos),
            (a + 1) + (a - 1) * cos - s);
    }

    public void SetHighShelf(int sampleRate, double frequency, double gainDb)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, 0.7071);
        double a = Math.Pow(10, gainDb / 40);
        double s = 2 * Math.Sqrt(a) * alpha;
        SetNormalized(
            a * ((a + 1) + (a - 1) * cos + s),
            -2 * a * ((a - 1) + (a + 1) * cos),
            a * ((a + 1) + (a - 1) * cos - s),
            (a + 1) - (a - 1) * cos + s,
            2 * ((a - 1) - (a + 1) * cos),
            (a + 1) - (a - 1) * cos - s);
    }

    public float Process(float x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return (float)y;
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed) return;
        double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2, z1 = _z1, z2 = _z2;
        for (int i = 0; i < buffer.Length; i++)
        {
            double x = buffer[i];
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            buffer[i] = (float)y;
        }
        _z1 = DspMath.FlushDenormal(z1);
        _z2 = DspMath.FlushDenormal(z2);
    }

    public void Reset() => _z1 = _z2 = 0;

    private static (double Cos, double Alpha) Prepare(int sampleRate, double frequency, double q)
    {
        double f = Math.Clamp(frequency, 10.0, sampleRate * 0.49);
        double w0 = 2 * Math.PI * f / sampleRate;
        return (Math.Cos(w0), Math.Sin(w0) / (2 * Math.Max(q, 0.05)));
    }

    private void SetNormalized(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
        _a1 = a1 / a0; _a2 = a2 / a0;
        IsBypassed = false;
    }
}
