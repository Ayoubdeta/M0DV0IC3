using M0DV0IC3.Dsp.Filters;

namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Quita la voz de una canción en estéreo para el karaoke. La voz principal casi siempre va en el centro de la
/// mezcla (igual en el canal izquierdo y en el derecho) y los instrumentos repartidos a los lados. Por cada
/// frecuencia (STFT de 2048 muestras, salto de 512), la mezcla central se baja hasta la potencia que tiene la
/// lateral (filtro de Wiener): lo que solo está en el centro desaparece y lo demás se queda. Solo en la zona de la
/// voz (de ~100 Hz a ~8 kHz): a diferencia del truco clásico de restar los canales, se conservan el bajo, el bombo y
/// los platillos.
/// <para>Medido con una mezcla de prueba (voz en el centro con eco estéreo, pad, guitarra, teclado, bajo, bombo y
/// platillos): la voz baja 24 dB (14 dB con una voz de hombre muy grave) y la música cambia -7,6 dB. La versión
/// anterior, que solo quitaba lo muy correlacionado, bajaba la voz unos 6 dB.</para>
/// <para>Entra estéreo y sale mono, como el resto del audio de la app. Latencia: 2048 muestras (43 ms a 48 kHz). Con
/// <see cref="Strength"/> = 0 la salida es la mezcla mono original, solo que retrasada.</para>
/// </summary>
public sealed class VocalRemover
{
    public const int FrameSize = 2048;
    private const int Hop = FrameSize / 4;
    private const int Bins = FrameSize / 2 + 1;

    // Suavizado de las estadísticas de cada frecuencia entre tramas (~25 ms): sin él, la máscara salta y suena a agua.
    private const double Smoothing = 0.6;

    private readonly Fft _fft = new(FrameSize);
    private readonly double[] _window = new double[FrameSize];
    private readonly double[] _bandWeight = new double[Bins];
    private readonly float[] _inLeft = new float[FrameSize];
    private readonly float[] _inRight = new float[FrameSize];
    private readonly double[] _overlap = new double[FrameSize];
    private readonly float[] _ready = new float[Hop];
    private readonly double[] _re = new double[FrameSize];
    private readonly double[] _im = new double[FrameSize];
    private readonly double[] _powerLeft = new double[Bins];
    private readonly double[] _powerRight = new double[Bins];
    private readonly double[] _cross = new double[Bins];
    private readonly double[] _gain = new double[Bins];
    private int _filled = FrameSize - Hop;
    private float _strength = 1f;

    public VocalRemover(int sampleRate = DspMath.SampleRate)
    {
        // Raíz de Hann en el análisis y en la síntesis: con un salto de N/4, las ventanas suman 2 en cada muestra.
        for (int i = 0; i < FrameSize; i++) _window[i] = Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FrameSize));

        // Solo la zona de la voz: sube de 70 a 110 Hz (las voces graves bajan de 100 Hz; el bajo y el bombo quedan
        // casi todos por debajo) y baja de 7 a 11 kHz.
        for (int k = 0; k < Bins; k++)
        {
            double hz = (double)k * sampleRate / FrameSize;
            double low = Math.Clamp((hz - 70) / 40, 0, 1);
            double high = Math.Clamp((11000 - hz) / 4000, 0, 1);
            _bandWeight[k] = low * high;
        }
    }

    /// <summary>Cuánta voz se quita, de 0 (nada) a 1 (todo lo que esté en el centro). Se puede cambiar desde otro hilo.</summary>
    public float Strength
    {
        get => Volatile.Read(ref _strength);
        set => Volatile.Write(ref _strength, Math.Clamp(value, 0f, 1f));
    }

    public int LatencySamples => FrameSize;

    /// <summary>Procesa un bloque: <paramref name="mono"/> recibe la canción sin voz, con <see cref="LatencySamples"/> de retraso.</summary>
    public void Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> mono)
    {
        for (int i = 0; i < left.Length; i++)
        {
            int slot = _filled - (FrameSize - Hop);
            mono[i] = _ready[slot];
            _inLeft[_filled] = left[i];
            _inRight[_filled] = right[i];
            if (++_filled == FrameSize)
            {
                ProcessFrame();
                _filled = FrameSize - Hop;
            }
        }
    }

    public void Reset()
    {
        Array.Clear(_inLeft);
        Array.Clear(_inRight);
        Array.Clear(_overlap);
        Array.Clear(_ready);
        Array.Clear(_powerLeft);
        Array.Clear(_powerRight);
        Array.Clear(_cross);
        _filled = FrameSize - Hop;
    }

    private void ProcessFrame()
    {
        // Una sola FFT para los dos canales: izquierdo en la parte real y derecho en la imaginaria.
        for (int i = 0; i < FrameSize; i++)
        {
            _re[i] = _inLeft[i] * _window[i];
            _im[i] = _inRight[i] * _window[i];
        }
        _fft.Forward(_re, _im);

        double strength = Strength;
        for (int k = 0; k < Bins; k++)
        {
            int m = (FrameSize - k) & (FrameSize - 1);
            double a = _re[k], b = _im[k], c = _re[m], d = _im[m];
            double lRe = 0.5 * (a + c), lIm = 0.5 * (b - d);
            double rRe = 0.5 * (b + d), rIm = -0.5 * (a - c);

            // Potencias de la mezcla central M = (L + R) / 2 y de la lateral S = (L - R) / 2.
            double mRe = 0.5 * (lRe + rRe), mIm = 0.5 * (lIm + rIm);
            double sRe = 0.5 * (lRe - rRe), sIm = 0.5 * (lIm - rIm);
            _powerLeft[k] = Smoothing * _powerLeft[k] + (1 - Smoothing) * (mRe * mRe + mIm * mIm);
            _powerRight[k] = Smoothing * _powerRight[k] + (1 - Smoothing) * (sRe * sRe + sIm * sIm);

            // Lo que suena a un lado, o distinto en cada canal, pone la misma potencia en M que en S; lo que está en el
            // centro solo pone potencia en M. Así que lo que no es del centro es una parte S / M de M, y esa es la
            // ganancia (filtro de Wiener): la voz del centro desaparece y lo demás se queda.
            double keep = Math.Min(1.0, _powerRight[k] / (_powerLeft[k] + 1e-20));
            _gain[k] = 1 - strength * _bandWeight[k] * (1 - keep);

            // De momento se guarda la mezcla mono (L + R) / 2 de esta frecuencia.
            _re[k] = 0.5 * (lRe + rRe);
            _im[k] = 0.5 * (lIm + rIm);
        }

        // Ganancias suavizadas entre frecuencias vecinas (menos "ruido musical") y espectro simétrico de una señal real.
        for (int k = 0; k < Bins; k++)
        {
            double g = (2 * _gain[k] + _gain[Math.Max(0, k - 1)] + _gain[Math.Min(Bins - 1, k + 1)]) / 4;
            _re[k] *= g;
            _im[k] *= g;
        }
        _im[0] = 0;
        _im[Bins - 1] = 0;
        for (int k = 1; k < Bins - 1; k++)
        {
            _re[FrameSize - k] = _re[k];
            _im[FrameSize - k] = -_im[k];
        }
        _fft.Inverse(_re, _im);

        // Solapar y sumar; las cuatro ventanas que se solapan suman 2.
        for (int i = 0; i < FrameSize; i++) _overlap[i] += _re[i] * _window[i] * 0.5;
        for (int i = 0; i < Hop; i++) _ready[i] = (float)_overlap[i];
        Array.Copy(_overlap, Hop, _overlap, 0, FrameSize - Hop);
        Array.Clear(_overlap, FrameSize - Hop, Hop);
        Array.Copy(_inLeft, Hop, _inLeft, 0, FrameSize - Hop);
        Array.Copy(_inRight, Hop, _inRight, 0, FrameSize - Hop);
        for (int k = 0; k < Bins; k++)
        {
            _powerLeft[k] = DspMath.FlushDenormal(_powerLeft[k]);
            _powerRight[k] = DspMath.FlushDenormal(_powerRight[k]);
            _cross[k] = DspMath.FlushDenormal(_cross[k]);
        }
    }
}
