namespace M0DV0IC3.Dsp.Effects;

/// <summary>Eco con realimentación (cueva, estadio). El buffer máximo se reserva al construir.</summary>
public sealed class Echo : IAudioEffect
{
    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private int _delay;
    private int _pos;
    private float _feedback;
    private float _mix;

    public Echo(int sampleRate, double maxDelayMs = 1500)
    {
        _sampleRate = sampleRate;
        _buffer = new float[(int)(sampleRate * maxDelayMs / 1000) + 1];
    }

    public bool IsActive => _mix > 0f && _delay > 0;

    public int LatencySamples => 0;

    public void Configure(double delayMs, double feedback, double mix)
    {
        _delay = Math.Clamp((int)(delayMs * _sampleRate / 1000), 0, _buffer.Length - 1);
        _feedback = (float)Math.Clamp(feedback, 0, 0.9);
        _mix = (float)Math.Clamp(mix, 0, 1);
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        int length = _buffer.Length;
        for (int i = 0; i < buffer.Length; i++)
        {
            int read = _pos - _delay;
            if (read < 0) read += length;
            float x = buffer[i];
            float delayed = _buffer[read];
            _buffer[_pos] = DspMath.FlushDenormal(x + _feedback * delayed);
            if (++_pos == length) _pos = 0;
            buffer[i] = x + _mix * delayed;
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _pos = 0;
    }
}
