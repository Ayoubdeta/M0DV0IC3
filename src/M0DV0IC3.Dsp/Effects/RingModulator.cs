namespace M0DV0IC3.Dsp.Effects;

/// <summary>Modulación en anillo: multiplica la voz por una senoidal (sonido metálico, robot o alien).</summary>
public sealed class RingModulator : IAudioEffect
{
    private readonly int _sampleRate;
    private double _phase;
    private double _increment;
    private float _mix;

    public RingModulator(int sampleRate) => _sampleRate = sampleRate;

    public bool IsActive => _mix > 0f && _increment > 0;

    public int LatencySamples => 0;

    public void Configure(double frequency, double mix)
    {
        _increment = frequency > 0 ? 2 * Math.PI * frequency / _sampleRate : 0;
        _mix = (float)Math.Clamp(mix, 0, 1);
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        float dry = 1f - _mix;
        for (int i = 0; i < buffer.Length; i++)
        {
            float carrier = (float)Math.Sin(_phase);
            buffer[i] *= dry + _mix * carrier;
            _phase += _increment;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
    }

    public void Reset() => _phase = 0;
}
