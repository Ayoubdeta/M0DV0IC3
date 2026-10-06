namespace M0DV0IC3.Dsp.Filters;

/// <summary>FFT compleja radix-2 en el sitio, con tablas precalculadas: no reserva memoria al transformar.</summary>
public sealed class Fft
{
    private readonly int _size;
    private readonly int[] _reversed;
    private readonly double[] _cos;
    private readonly double[] _sin;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("El tamaño debe ser una potencia de 2.", nameof(size));
        _size = size;
        int bits = (int)Math.Log2(size);
        _reversed = new int[size];
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _reversed[i] = r;
        }
        _cos = new double[size / 2];
        _sin = new double[size / 2];
        for (int k = 0; k < size / 2; k++)
        {
            _cos[k] = Math.Cos(2 * Math.PI * k / size);
            _sin[k] = -Math.Sin(2 * Math.PI * k / size);
        }
    }

    public int Size => _size;

    public void Forward(Span<double> re, Span<double> im) => Transform(re, im, inverse: false);

    /// <summary>Transformada inversa, ya dividida por el tamaño.</summary>
    public void Inverse(Span<double> re, Span<double> im)
    {
        Transform(re, im, inverse: true);
        double scale = 1.0 / _size;
        for (int i = 0; i < _size; i++)
        {
            re[i] *= scale;
            im[i] *= scale;
        }
    }

    private void Transform(Span<double> re, Span<double> im, bool inverse)
    {
        if (re.Length != _size || im.Length != _size) throw new ArgumentException("Tamaño distinto del de la FFT.");
        for (int i = 0; i < _size; i++)
        {
            int j = _reversed[i];
            if (j <= i) continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }

        double sign = inverse ? -1 : 1;
        for (int length = 2; length <= _size; length <<= 1)
        {
            int half = length / 2, step = _size / length;
            for (int start = 0; start < _size; start += length)
            {
                for (int k = 0; k < half; k++)
                {
                    double wr = _cos[k * step], wi = sign * _sin[k * step];
                    int a = start + k, b = a + half;
                    double tr = re[b] * wr - im[b] * wi;
                    double ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
