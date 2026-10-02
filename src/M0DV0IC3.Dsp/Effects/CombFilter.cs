namespace M0DV0IC3.Dsp.Effects;

/// <summary>Filtro comb realimentado de retardo corto: resonancia metálica (robot, lata).</summary>
public sealed class CombFilter : IAudioEffect
{
    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private int _delay;
    private int _pos;
    private float _feedback;
    private float _mix;

    public CombFilter(int sampleRate, double maxDelayMs = 50)
    {
        _sampleRate = sampleRate;
        _buffer = new float[(int)(sampleRate * maxDelayMs / 1000) + 1];
    }

    public bool IsActive => _mix > 0f && _delay > 0;

    public int LatencySamples => 0;

    public void Configure(double delayMs, double feedback, double mix)
    {
        _delay = Math.Clamp((int)(delayMs * _sampleRate / 1000), 0, _buffer.Length - 1);
        _feedback = (float)Math.Clamp(feedback, 0, 0.95);
        _mix = (float)Math.Clamp(mix, 0, 1);
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        int length = _buffer.Length;
        float wetGain = _mix * (1f - _feedback);
        float dry = 1f - _mix;
        for (int i = 0; i < buffer.Length; i++)
        {
            int read = _pos - _delay;
            if (read < 0) read += length;
            float x = buffer[i];
            float y = x + _feedback * _buffer[read];
            _buffer[_pos] = DspMath.FlushDenormal(y);
            if (++_pos == length) _pos = 0;
            buffer[i] = dry * x + wetGain * y;
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _pos = 0;
    }
}
