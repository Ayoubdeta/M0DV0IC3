using M0DV0IC3.Dsp;

namespace M0DV0IC3.Cli;

/// <summary>Análisis espectral sencillo: FFT radix-2 con ventana Hann.</summary>
internal sealed class Spectrum
{
    private readonly int _size;
    private readonly double[] _window;
    private readonly double[] _cos;
    private readonly double[] _sin;
    private readonly int[] _bitReverse;
    private readonly double[] _re;
    private readonly double[] _im;

    public Spectrum(int size = 2048)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("El tamaño debe ser potencia de 2", nameof(size));
        _size = size;
        _window = new double[size];
        for (int i = 0; i < size; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
        _cos = new double[size / 2];
        _sin = new double[size / 2];
        for (int k = 0; k < size / 2; k++)
        {
            _cos[k] = Math.Cos(-2 * Math.PI * k / size);
            _sin[k] = Math.Sin(-2 * Math.PI * k / size);
        }
        _bitReverse = new int[size];
        int bits = (int)Math.Log2(size);
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _bitReverse[i] = r;
        }
        _re = new double[size];
        _im = new double[size];
    }

    public int Size => _size;

    /// <summary>
    /// Centroide espectral (Hz) entre 50 Hz y <paramref name="maxHz"/>, ponderado por magnitud, sobre tramas con
    /// solape del 50 %. <paramref name="include"/> recibe la muestra central de cada trama y decide si cuenta.
    /// </summary>
    public double Centroid(ReadOnlySpan<float> x, Func<int, bool>? include = null, double maxHz = 8000)
    {
        int minBin = Math.Max(1, (int)(50.0 * _size / DspMath.SampleRate));
        int maxBin = Math.Min(_size / 2 - 1, (int)(maxHz * _size / DspMath.SampleRate));
        double weighted = 0, total = 0;
        for (int start = 0; start + _size <= x.Length; start += _size / 2)
        {
            if (include is not null && !include(start + _size / 2)) continue;
            Transform(x.Slice(start, _size));
            for (int k = minBin; k <= maxBin; k++)
            {
                double magnitude = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
                weighted += magnitude * k * DspMath.SampleRate / _size;
                total += magnitude;
            }
        }
        return total > 0 ? weighted / total : double.NaN;
    }

    private void Transform(ReadOnlySpan<float> frame)
    {
        for (int i = 0; i < _size; i++)
        {
            int j = _bitReverse[i];
            _re[j] = frame[i] * _window[i];
            _im[j] = 0;
        }

        for (int length = 2; length <= _size; length <<= 1)
        {
            int half = length / 2;
            int stride = _size / length;
            for (int i = 0; i < _size; i += length)
            {
                for (int k = 0; k < half; k++)
                {
                    double wr = _cos[k * stride], wi = _sin[k * stride];
                    int a = i + k, b = a + half;
                    double tr = _re[b] * wr - _im[b] * wi;
                    double ti = _re[b] * wi + _im[b] * wr;
                    _re[b] = _re[a] - tr;
                    _im[b] = _im[a] - ti;
                    _re[a] += tr;
                    _im[a] += ti;
                }
            }
        }
    }
}
