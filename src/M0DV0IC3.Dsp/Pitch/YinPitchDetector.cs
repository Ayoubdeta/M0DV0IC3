using M0DV0IC3.Dsp.Filters;

namespace M0DV0IC3.Dsp.Pitch;

/// <summary>
/// Detector de tono YIN (de Cheveigné y Kawahara, 2002) en streaming.
/// Para que sea barato, la señal se filtra paso bajo y se diezma x4 (48 kHz → 12 kHz) antes de analizar.
/// Cada <c>hopSamples</c> muestras de entrada vuelve a estimar el periodo sobre la ventana más reciente.
/// </summary>
public sealed class YinPitchDetector
{
    public const int Decimation = 4;

    private const int HistorySize = 1024;
    private const int HistoryMask = HistorySize - 1;
    private const float SilenceRms = 0.0025f;

    private readonly Biquad _antiAlias1 = new();
    private readonly Biquad _antiAlias2 = new();
    private readonly float[] _history = new float[HistorySize];
    private readonly float[] _frame;
    private readonly float[] _cmnd;
    private readonly double[] _recent = new double[3];
    private readonly int _tauMin;
    private readonly int _tauMax;
    private readonly int _window;
    private readonly int _hopDecimated;
    private readonly double _threshold;

    private int _historyPos;
    private int _filled;
    private int _decimationPhase;
    private int _sinceAnalysis;
    private int _recentCount;
    private double _period;
    private double _onsetCandidate;

    public YinPitchDetector(int sampleRate, double minFrequency, double maxFrequency, int hopSamples = 256, double threshold = 0.15)
    {
        double decimatedRate = (double)sampleRate / Decimation;
        _tauMax = (int)Math.Ceiling(decimatedRate / minFrequency) + 1;
        _tauMin = Math.Max(2, (int)Math.Floor(decimatedRate / maxFrequency));
        _window = _tauMax;
        if (_window + _tauMax + 1 > HistorySize) throw new ArgumentOutOfRangeException(nameof(minFrequency));

        _hopDecimated = Math.Max(1, hopSamples / Decimation);
        _threshold = threshold;
        _frame = new float[_window + _tauMax + 1];
        _cmnd = new float[_tauMax + 2];
        _period = sampleRate / 150.0;

        _antiAlias1.SetLowPass(sampleRate, 1800, 0.5412);
        _antiAlias2.SetLowPass(sampleRate, 1800, 1.3066);
    }

    /// <summary>Periodo estimado en muestras a la frecuencia de entrada (no la diezmada).</summary>
    public double PeriodSamples => _period;

    public bool IsVoiced { get; private set; }

    /// <summary>1 - d'(τ) del último análisis: cerca de 1 indica una voz muy periódica.</summary>
    public float Confidence { get; private set; }

    /// <summary>Envía una muestra y devuelve la señal filtrada paso bajo, que PSOLA usa para alinear marcas.</summary>
    public float Push(float x)
    {
        float lowPassed = _antiAlias2.Process(_antiAlias1.Process(x));
        if (++_decimationPhase == Decimation)
        {
            _decimationPhase = 0;
            _history[_historyPos] = lowPassed;
            _historyPos = (_historyPos + 1) & HistoryMask;
            if (_filled < HistorySize) _filled++;

            if (++_sinceAnalysis >= _hopDecimated)
            {
                _sinceAnalysis = 0;
                if (_filled >= _frame.Length) Analyze();
            }
        }
        return lowPassed;
    }

    public void Reset()
    {
        Array.Clear(_history);
        _antiAlias1.Reset();
        _antiAlias2.Reset();
        _historyPos = _filled = _decimationPhase = _sinceAnalysis = _recentCount = 0;
        _onsetCandidate = 0;
        IsVoiced = false;
        Confidence = 0;
    }

    private void Analyze()
    {
        int length = _frame.Length;
        int start = (_historyPos - length) & HistoryMask;
        double energy = 0;
        for (int i = 0; i < length; i++)
        {
            float v = _history[(start + i) & HistoryMask];
            _frame[i] = v;
            energy += v * v;
        }

        if (Math.Sqrt(energy / length) < SilenceRms)
        {
            MarkUnvoiced();
            return;
        }

        // Función diferencia normalizada por la media acumulada (pasos 2 y 3 de YIN).
        ReadOnlySpan<float> frame = _frame;
        _cmnd[0] = 1f;
        double running = 0;
        for (int tau = 1; tau <= _tauMax; tau++)
        {
            float d = SquaredDifference(frame[.._window], frame.Slice(tau, _window));
            running += d;
            _cmnd[tau] = running > 0 ? (float)(d * tau / running) : 1f;
        }

        // Paso 4: primer mínimo por debajo del umbral (evita errores de octava).
        int best = -1;
        for (int tau = _tauMin; tau < _tauMax; tau++)
        {
            if (_cmnd[tau] < _threshold)
            {
                while (tau + 1 < _tauMax && _cmnd[tau + 1] < _cmnd[tau]) tau++;
                best = tau;
                break;
            }
        }

        // Una periodicidad más débil solo vale para continuar un tramo sonoro sin cambiar mucho de tono.
        // Al final de las vocales la energía cae y, sin esta condición, aparecían falsos tonos agudos.
        if (best < 0 && IsVoiced)
        {
            int argMin = _tauMin;
            for (int tau = _tauMin + 1; tau < _tauMax; tau++)
                if (_cmnd[tau] < _cmnd[argMin]) argMin = tau;
            double ratio = argMin * Decimation / _period;
            if (_cmnd[argMin] < 0.3f && ratio > 0.77 && ratio < 1.3) best = argMin;
        }

        if (best < 0)
        {
            MarkUnvoiced();
            return;
        }

        // Paso 5: interpolación parabólica del mínimo, sin salirse del rango de búsqueda.
        double refined = best;
        if (best > 1 && best < _tauMax)
        {
            double s0 = _cmnd[best - 1], s1 = _cmnd[best], s2 = _cmnd[best + 1];
            double denominator = s0 - 2 * s1 + s2;
            if (Math.Abs(denominator) > 1e-12) refined = best + 0.5 * (s0 - s2) / denominator;
        }
        double estimate = Math.Clamp(refined, _tauMin, _tauMax) * Decimation;

        if (!IsVoiced)
        {
            // Un tramo sonoro empieza con dos estimaciones seguidas coherentes (±20 %), no con una sola.
            if (_onsetCandidate > 0 && Math.Abs(estimate / _onsetCandidate - 1) < 0.2)
            {
                IsVoiced = true;
                Confidence = 1f - _cmnd[best];
                Smooth(_onsetCandidate);
                _period = Smooth(estimate);
                _onsetCandidate = 0;
            }
            else
            {
                _onsetCandidate = estimate;
            }
            return;
        }

        Confidence = 1f - _cmnd[best];
        _period = Smooth(estimate);
    }

    private void MarkUnvoiced()
    {
        IsVoiced = false;
        Confidence = 0;
        _recentCount = 0;
        _onsetCandidate = 0;
    }

    /// <summary>Mediana de las 3 últimas estimaciones sonoras: elimina saltos de octava aislados.</summary>
    private double Smooth(double estimate)
    {
        _recent[_recentCount % 3] = estimate;
        _recentCount++;
        if (_recentCount < 3) return estimate;
        double a = _recent[0], b = _recent[1], c = _recent[2];
        return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
    }

    private static float SquaredDifference(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            float d = a[i] - b[i];
            sum += d * d;
        }
        return sum;
    }
}
