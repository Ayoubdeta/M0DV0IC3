using System.Diagnostics;

namespace M0DV0IC3.Audio.Realtime;

/// <summary>Fuente de audio mono que consume un hilo de salida.</summary>
public interface IRenderSource
{
    void Render(Span<float> destination);
}

/// <summary>
/// Lee de un <see cref="SpscRingBuffer"/> y mantiene su nivel lo más bajo posible sin que haya cortes.
/// <para>
/// El micrófono y la salida tienen relojes distintos que derivan (±100 ppm típicos) y entregan y piden
/// bloques de tamaños y fases distintos. En vez de dejar un colchón grande, se mide el nivel <b>mínimo</b>
/// del buffer en cada ventana de 250 ms y se ajusta la velocidad de lectura (±0,5 % como mucho, con
/// interpolación Catmull-Rom). Así ese mínimo queda en el margen de seguridad (por defecto 2 ms).
/// Si sobra mucho (tras un tirón), se descarta el exceso de golpe.
/// </para>
/// </summary>
public sealed class DriftCompensatedReader : IRenderSource
{
    private const double Gain = 1e-5;
    private const double MaxCorrection = 0.005;

    private readonly SpscRingBuffer _ring;
    private readonly int _targetMinFill;
    private readonly int _windowLength;
    private readonly int _hardSkipThreshold;
    private readonly long _warmupSamples;

    private double _ratio = 1.0;
    private double _frac;
    private float _xm1, _x0, _x1, _x2;
    private bool _priming = true;
    private int _windowMin = int.MaxValue;
    private int _windowSamples;
    private double _fillAccumulator;
    private int _fillCount;
    private double _waitTicks;
    private long _waitSamples;
    private int _underruns;
    private int _warmupUnderruns;
    private long _rendered;
    private float _averageFill;
    private float _averageWaitMs;

    /// <param name="warmupSeconds">Al principio, mientras los dos dispositivos se sincronizan, es normal algún corte: se cuentan aparte.</param>
    public DriftCompensatedReader(SpscRingBuffer ring, int targetMinFill, int sampleRate = 48000, double warmupSeconds = 1.5)
    {
        _warmupSamples = (long)(warmupSeconds * sampleRate);
        _ring = ring;
        _targetMinFill = Math.Max(0, targetMinFill);
        _windowLength = sampleRate / 4;
        // Más de 5 ms de exceso sobre el mínimo no se debe a la deriva (eso son décimas de ms por segundo),
        // sino al arranque o a un tirón: se recorta de golpe en vez de tardar segundos con el ajuste fino.
        _hardSkipThreshold = sampleRate / 200;
    }

    /// <summary>Número de veces que el buffer se quedó vacío (cortes audibles), incluidos los del arranque.</summary>
    public int Underruns => Volatile.Read(ref _underruns);

    /// <summary>Los de <see cref="Underruns"/> que ocurrieron en el arranque (los primeros segundos).</summary>
    public int WarmupUnderruns => Volatile.Read(ref _warmupUnderruns);

    /// <summary>Nivel medio del buffer justo después de cada lectura, en muestras.</summary>
    public float AverageFill => Volatile.Read(ref _averageFill);

    /// <summary>
    /// Lo que espera de media cada muestra en el buffer, desde que llega del micro hasta que la recoge la salida,
    /// en ms. Incluye el desfase entre los dos dispositivos, que <see cref="AverageFill"/> no ve.
    /// </summary>
    public float AverageWaitMs => Volatile.Read(ref _averageWaitMs);

    public double Ratio => _ratio;

    public void Render(Span<float> destination) => Render(destination, Stopwatch.GetTimestamp());

    /// <param name="now">Momento actual en ticks de <see cref="Stopwatch"/> (los tests lo simulan).</param>
    public void Render(Span<float> destination, long now)
    {
        int n = destination.Length;
        if (n == 0) return;
        bool warmingUp = _rendered < _warmupSamples;
        _rendered += n;

        int available = _ring.Available;
        if (_priming)
        {
            if (available < _targetMinFill + n)
            {
                destination.Clear();
                return;
            }
            _priming = false;
        }

        double ratio = _ratio;
        double frac = _frac;
        int consumed = 0;
        int i = 0;
        for (; i < n; i++)
        {
            while (frac >= 1.0)
            {
                if (consumed == available) goto Underrun;
                _xm1 = _x0; _x0 = _x1; _x1 = _x2;
                _x2 = _ring.PeekAt(consumed++);
                frac -= 1.0;
            }
            destination[i] = CatmullRom(_xm1, _x0, _x1, _x2, (float)frac);
            frac += ratio;
        }

        _frac = frac;
        MeasureWait(consumed, now);
        _ring.Advance(consumed);
        Track(available - consumed, n);
        return;

    Underrun:
        destination[i..].Clear();
        MeasureWait(consumed, now);
        _ring.Advance(consumed);
        _frac = frac;
        _priming = true;
        Interlocked.Increment(ref _underruns);
        if (warmingUp) Interlocked.Increment(ref _warmupUnderruns);
        ResetWindow();
    }

    private void Track(int fillAfterRead, int samples)
    {
        if (fillAfterRead < _windowMin) _windowMin = fillAfterRead;
        _fillAccumulator += fillAfterRead;
        _fillCount++;
        _windowSamples += samples;
        if (_windowSamples < _windowLength) return;

        int error = _windowMin - _targetMinFill;
        if (error > _hardSkipThreshold)
        {
            // Sobran muchas muestras (arranque, o la salida se bloqueó): se recupera la latencia baja ya.
            _ring.Advance(error);
            _ratio = 1.0;
        }
        else
        {
            _ratio = 1.0 + Math.Clamp(error * Gain, -MaxCorrection, MaxCorrection);
        }

        Volatile.Write(ref _averageFill, (float)(_fillAccumulator / Math.Max(1, _fillCount)));
        if (_waitSamples > 0)
            Volatile.Write(ref _averageWaitMs, (float)(_waitTicks / _waitSamples * 1000.0 / Stopwatch.Frequency));
        ResetWindow();
    }

    private void MeasureWait(int consumed, long now)
    {
        if (consumed <= 0) return;
        _waitTicks += _ring.SumWaitTicks(_ring.ReadPosition, consumed, now);
        _waitSamples += consumed;
    }

    private void ResetWindow()
    {
        _windowMin = int.MaxValue;
        _windowSamples = 0;
        _fillAccumulator = 0;
        _fillCount = 0;
        _waitTicks = 0;
        _waitSamples = 0;
    }

    private static float CatmullRom(float xm1, float x0, float x1, float x2, float t)
    {
        float c1 = 0.5f * (x1 - xm1);
        float c2 = xm1 - 2.5f * x0 + 2f * x1 - 0.5f * x2;
        float c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        return ((c3 * t + c2) * t + c1) * t + x0;
    }
}
