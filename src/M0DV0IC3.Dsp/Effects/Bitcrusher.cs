namespace M0DV0IC3.Dsp.Effects;

/// <summary>Reduce la frecuencia de muestreo (sample &amp; hold) y/o la resolución en bits: sonido de teléfono o de 8 bits.</summary>
public sealed class Bitcrusher : IAudioEffect
{
    private readonly int _sampleRate;
    private double _step;
    private double _phase;
    private float _held;
    private float _levels;

    public Bitcrusher(int sampleRate) => _sampleRate = sampleRate;

    public bool IsActive => _step > 0 || _levels > 0;

    public int LatencySamples => 0;

    public void Configure(double targetRate, int bits)
    {
        _step = targetRate > 0 && targetRate < _sampleRate ? targetRate / _sampleRate : 0;
        _levels = bits is > 0 and < 24 ? MathF.Pow(2, bits - 1) : 0;
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            if (_step > 0)
            {
                _phase += _step;
                if (_phase >= 1.0)
                {
                    _phase -= 1.0;
                    _held = x;
                }
                x = _held;
            }
            if (_levels > 0) x = MathF.Round(x * _levels) / _levels;
            buffer[i] = x;
        }
    }

    public void Reset()
    {
        _phase = 0;
        _held = 0;
    }
}
