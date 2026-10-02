namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Flanger: copia de la voz con un retardo muy corto (0,3-4,3 ms) que barre lentamente y se realimenta.
/// Las resonancias del filtro comb suben y bajan: el clásico sonido de avión a reacción. Latencia cero.
/// </summary>
public sealed class Flanger : IAudioEffect
{
    private const double MinDelayMs = 0.3;
    private const double SweepMs = 4.0;
    private const float Feedback = 0.6f;

    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private readonly double _minDelay;
    private readonly double _sweep;
    private double _phase;
    private double _increment;
    private float _mix;
    private int _pos;

    public Flanger(int sampleRate)
    {
        _sampleRate = sampleRate;
        _minDelay = MinDelayMs * sampleRate / 1000;
        _sweep = SweepMs * sampleRate / 1000;
        _buffer = new float[(int)(_minDelay + _sweep) + 4];
    }

    public bool IsActive => _mix > 0f;

    public int LatencySamples => 0;

    /// <param name="mix">0..1, cantidad de efecto.</param>
    /// <param name="rateHz">Barridos por segundo (0,05-5 Hz).</param>
    public void Configure(double mix, double rateHz)
    {
        _mix = (float)Math.Clamp(mix, 0, 1);
        _increment = 2 * Math.PI * Math.Clamp(rateHz, 0.05, 5) / _sampleRate;
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        int length = _buffer.Length;
        float dryGain = 1f - 0.5f * _mix;
        float wetGain = 0.5f * _mix;
        for (int i = 0; i < buffer.Length; i++)
        {
            double delay = _minDelay + _sweep * 0.5 * (1 - Math.Cos(_phase));
            double read = _pos - delay;
            if (read < 0) read += length;
            int i0 = (int)read;
            float frac = (float)(read - i0);
            int i1 = i0 + 1 == length ? 0 : i0 + 1;
            float delayed = _buffer[i0] + (_buffer[i1] - _buffer[i0]) * frac;

            float x = buffer[i];
            _buffer[_pos] = DspMath.FlushDenormal(x + Feedback * delayed);
            if (++_pos == length) _pos = 0;
            buffer[i] = dryGain * x + wetGain * delayed;

            _phase += _increment;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _pos = 0;
        _phase = 0;
    }
}
