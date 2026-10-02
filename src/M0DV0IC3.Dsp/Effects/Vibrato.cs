namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// LFO algo irregular: dos senos con frecuencias en proporción 1 : 1,618, que nunca se repiten igual.
/// Un vibrato o un balanceo así suena más orgánico que un seno perfecto. El valor está siempre en [-1, 1].
/// </summary>
internal struct WobbleLfo
{
    /// <summary>Pendiente máxima de <see cref="Value"/> respecto de la fase del primer seno.</summary>
    public const double MaxSlope = 0.7 + 0.3 * SecondRatio;

    private const double SecondRatio = 1.618;
    private const double TwoPi = 2 * Math.PI;

    private double _phase1;
    private double _phase2;

    /// <summary>Radianes por muestra del primer seno.</summary>
    public double Increment { get; private set; }

    public readonly double Value => 0.7 * Math.Sin(_phase1) + 0.3 * Math.Sin(_phase2);

    public void SetRate(double hz, int sampleRate) => Increment = TwoPi * Math.Max(0, hz) / sampleRate;

    public void Advance(double samples)
    {
        _phase1 += Increment * samples;
        _phase2 += Increment * SecondRatio * samples;
        if (_phase1 >= TwoPi) _phase1 %= TwoPi;
        if (_phase2 >= TwoPi) _phase2 %= TwoPi;
    }

    public void Reset() => _phase1 = _phase2 = 0;
}

/// <summary>
/// Vibrato por retardo modulado (efecto Doppler), para las voces sin PSOLA; con PSOLA el vibrato se hace en los
/// granos y no añade latencia. Aquí el retardo medio es la latencia: ~1 ms para un vibrato rápido y suave, hasta
/// 20 ms para uno muy lento y profundo.
/// </summary>
public sealed class Vibrato : IAudioEffect
{
    private const double MaxAmplitudeMs = 20;

    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private readonly double _maxAmplitude;
    private WobbleLfo _lfo;
    private double _amplitude;
    private double _targetAmplitude;
    private bool _started;
    private int _pos;

    public Vibrato(int sampleRate)
    {
        _sampleRate = sampleRate;
        _maxAmplitude = MaxAmplitudeMs * sampleRate / 1000;
        _buffer = new float[(int)(2 * _maxAmplitude) + 4];
    }

    public bool IsActive => _targetAmplitude > 0 || _amplitude > 0.01;

    public int LatencySamples => (int)Math.Round(_targetAmplitude);

    /// <param name="rateHz">Oscilaciones por segundo.</param>
    /// <param name="depthSemitones">Desviación máxima del tono, en semitonos.</param>
    public void Configure(double rateHz, double depthSemitones)
    {
        if (rateHz <= 0 || depthSemitones <= 0)
        {
            _targetAmplitude = 0;
            return;
        }
        _lfo.SetRate(Math.Min(rateHz, 20), _sampleRate);
        // El tono se desvía en la derivada del retardo: amplitud × pendiente máxima del LFO.
        double deviation = DspMath.SemitonesToRatio(Math.Min(depthSemitones, 3)) - 1;
        _targetAmplitude = Math.Min(deviation / (_lfo.Increment * WobbleLfo.MaxSlope), _maxAmplitude);
        if (!_started) _amplitude = _targetAmplitude;
    }

    public void Process(Span<float> buffer)
    {
        int length = _buffer.Length;
        _started = true;
        if (!IsActive)
        {
            // Se sigue llenando el buffer: si se activa en caliente, ya tiene el audio reciente y no hay un hueco.
            for (int i = 0; i < buffer.Length; i++)
            {
                _buffer[_pos] = buffer[i];
                if (++_pos == length) _pos = 0;
            }
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _buffer[_pos] = buffer[i];

            // La amplitud cambia despacio: al mover el slider no hay saltos de retardo (clics).
            _amplitude += 0.0005 * (_targetAmplitude - _amplitude);
            double read = _pos - _amplitude * (1 + _lfo.Value);
            if (read < 0) read += length;
            int i0 = (int)read;
            float frac = (float)(read - i0);
            int i1 = i0 + 1 == length ? 0 : i0 + 1;
            buffer[i] = _buffer[i0] + (_buffer[i1] - _buffer[i0]) * frac;

            if (++_pos == length) _pos = 0;
            _lfo.Advance(1);
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _pos = 0;
        _amplitude = _targetAmplitude;
        _lfo.Reset();
    }
}
