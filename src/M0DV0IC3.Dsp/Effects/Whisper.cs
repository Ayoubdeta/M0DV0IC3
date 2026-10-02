namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Susurro: cambia la vibración de las cuerdas vocales por ruido y conserva la forma de la boca (los formantes),
/// que es lo que hace un susurro de verdad.
/// <list type="bullet">
/// <item>Cada 5,3 ms se calcula la envolvente LPC de orden 28 de los últimos 21 ms (preénfasis, ventana Hann,
/// autocorrelación y Levinson-Durbin).</item>
/// <item>Un filtro en celosía la imprime sobre ruido blanco, con el mismo nivel que la voz.</item>
/// <item>Los coeficientes de reflexión se interpolan muestra a muestra entre análisis. Con |k| &lt; 1, el filtro
/// es siempre estable.</item>
/// </list>
/// Solo usa audio pasado: latencia cero.
/// </summary>
public sealed class Whisper : IAudioEffect
{
    private const int Order = 28;
    private const int Window = 1024;
    private const int WindowMask = Window - 1;
    private const int Hop = 256;
    private const float PreEmphasis = 0.9f;

    private readonly float[] _history = new float[Window];
    private readonly double[] _window = new double[Window];
    private readonly double[] _frame = new double[Window];
    private readonly double[] _lagWindow = new double[Order + 1];
    private readonly double[] _r = new double[Order + 1];
    private readonly double[] _a = new double[Order + 1];
    private readonly double[] _previousA = new double[Order + 1];
    private readonly float[] _k = new float[Order + 1];
    private readonly float[] _kTarget = new float[Order + 1];
    private readonly float[] _kStep = new float[Order + 1];
    private readonly float[] _backward = new float[Order + 1];
    private readonly double _windowEnergy;

    private int _historyPos;
    private int _sinceAnalysis;
    private float _gain;
    private float _gainStep;
    private float _previousInput;
    private float _deEmphasis;
    private uint _seed = 0x2545F491;
    private float _mix;

    public Whisper(int sampleRate)
    {
        double energy = 0;
        for (int i = 0; i < Window; i++)
        {
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 0.5) / Window);
            energy += _window[i] * _window[i];
        }
        _windowEnergy = energy;

        // Ventana de retardo gaussiana (~80 Hz): formantes algo más anchos, que suenan más suaves y son más estables.
        for (int i = 0; i <= Order; i++)
        {
            double x = 2 * Math.PI * 80.0 * i / sampleRate;
            _lagWindow[i] = Math.Exp(-0.5 * x * x);
        }
        _lagWindow[0] = 1.0001; // corrección de ruido blanco: evita matrices casi singulares con tonos puros
    }

    public bool IsActive => _mix > 0f;

    public int LatencySamples => 0;

    /// <param name="mix">0 = voz normal, 1 = solo susurro.</param>
    public void Configure(double mix) => _mix = (float)Math.Clamp(mix, 0, 1);

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        float dry = 1f - _mix;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            _history[_historyPos] = x - PreEmphasis * _previousInput;
            _previousInput = x;
            _historyPos = (_historyPos + 1) & WindowMask;
            if (++_sinceAnalysis >= Hop)
            {
                _sinceAnalysis = 0;
                Analyze();
            }

            // xorshift32 → ruido uniforme de varianza 1 (amplitud √3).
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            float f = 1.7320508f * (_seed / 2147483648f - 1f) * _gain;
            _gain += _gainStep;

            // Celosía todo-polos: f_{m-1} = f_m - k_m·b_{m-1}[n-1];  b_m[n] = k_m·f_{m-1} + b_{m-1}[n-1].
            for (int m = Order; m >= 1; m--)
            {
                float k = _k[m];
                f -= k * _backward[m - 1];
                _backward[m] = k * f + _backward[m - 1];
                _k[m] = k + _kStep[m];
            }
            _backward[0] = f;

            _deEmphasis = f + PreEmphasis * _deEmphasis;
            buffer[i] = dry * x + _mix * _deEmphasis;
        }

        _deEmphasis = DspMath.FlushDenormal(_deEmphasis);
        for (int m = 0; m <= Order; m++) _backward[m] = DspMath.FlushDenormal(_backward[m]);
    }

    public void Reset()
    {
        Array.Clear(_history);
        Array.Clear(_k);
        Array.Clear(_kStep);
        Array.Clear(_backward);
        _historyPos = _sinceAnalysis = 0;
        _gain = _gainStep = 0;
        _previousInput = _deEmphasis = 0;
    }

    private void Analyze()
    {
        // Ventana más reciente, la muestra más antigua primero.
        for (int n = 0; n < Window; n++) _frame[n] = _history[(_historyPos + n) & WindowMask] * _window[n];
        for (int lag = 0; lag <= Order; lag++)
        {
            double sum = 0;
            for (int n = lag; n < Window; n++) sum += _frame[n] * _frame[n - lag];
            _r[lag] = sum * _lagWindow[lag];
        }

        // Levinson-Durbin, con A(z) = 1 + Σ a_i z^-i. Los k_m son los coeficientes de la celosía. Si una etapa no
        // es válida (señal casi nula o casi un seno puro), esa y las siguientes se quedan en 0: filtro más suave.
        Array.Clear(_kTarget);
        Array.Clear(_a);
        _a[0] = 1;
        double error = _r[0];
        double targetGain = 0;
        if (error > 1e-10)
        {
            for (int m = 1; m <= Order; m++)
            {
                double acc = _r[m];
                for (int j = 1; j < m; j++) acc += _a[j] * _r[m - j];
                double k = -acc / error;
                if (!double.IsFinite(k) || Math.Abs(k) >= 0.999) break;

                Array.Copy(_a, _previousA, m);
                for (int j = 1; j < m; j++) _a[j] = _previousA[j] + k * _previousA[m - j];
                _a[m] = k;
                error *= 1 - k * k;
                _kTarget[m] = (float)k;
            }
            // Ruido de varianza σ² = error / Σw² por muestra: la misma potencia que la voz de la ventana.
            targetGain = Math.Sqrt(Math.Max(error, 0) / _windowEnergy);
        }

        for (int m = 1; m <= Order; m++) _kStep[m] = (_kTarget[m] - _k[m]) / Hop;
        _gainStep = ((float)targetGain - _gain) / Hop;
    }
}
