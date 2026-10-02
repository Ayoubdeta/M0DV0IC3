namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Chorus de tres voces: copias retardadas 12, 18 y 25 ms, cada una modulada por su propio LFO lento
/// (frecuencias no múltiplos entre sí). Suena a varias personas hablando a la vez. Latencia cero.
/// </summary>
public sealed class Chorus : IAudioEffect
{
    private const int Voices = 3;
    private const double DepthMs = 3;
    private const float VoiceGain = 0.5f;

    private static readonly double[] BaseDelaysMs = [12, 18, 25];
    private static readonly double[] RatesHz = [0.9, 0.67, 1.13];
    private static readonly double[] StartPhases = [0, 2.1, 4.2];

    private readonly float[] _buffer;
    private readonly double[] _baseDelays = new double[Voices];
    private readonly double[] _increments = new double[Voices];
    private readonly double[] _phases = new double[Voices];
    private readonly double _depth;
    private int _pos;
    private float _mix;

    public Chorus(int sampleRate)
    {
        _depth = DepthMs * sampleRate / 1000;
        double maxDelay = 0;
        for (int v = 0; v < Voices; v++)
        {
            _baseDelays[v] = BaseDelaysMs[v] * sampleRate / 1000;
            _increments[v] = 2 * Math.PI * RatesHz[v] / sampleRate;
            maxDelay = Math.Max(maxDelay, _baseDelays[v] + _depth);
        }
        _buffer = new float[(int)maxDelay + 4];
        Reset();
    }

    public bool IsActive => _mix > 0f;

    public int LatencySamples => 0;

    public void Configure(double mix) => _mix = (float)Math.Clamp(mix, 0, 1);

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        int length = _buffer.Length;
        float dryGain = 1f - 0.5f * _mix;
        float wetGain = 0.5f * _mix * VoiceGain;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            _buffer[_pos] = x;

            float wet = 0f;
            for (int v = 0; v < Voices; v++)
            {
                double read = _pos - (_baseDelays[v] + _depth * Math.Sin(_phases[v]));
                if (read < 0) read += length;
                int i0 = (int)read;
                float frac = (float)(read - i0);
                int i1 = i0 + 1 == length ? 0 : i0 + 1;
                wet += _buffer[i0] + (_buffer[i1] - _buffer[i0]) * frac;

                double phase = _phases[v] + _increments[v];
                _phases[v] = phase > 2 * Math.PI ? phase - 2 * Math.PI : phase;
            }

            buffer[i] = dryGain * x + wetGain * wet;
            if (++_pos == length) _pos = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _pos = 0;
        for (int v = 0; v < Voices; v++) _phases[v] = StartPhases[v];
    }
}
