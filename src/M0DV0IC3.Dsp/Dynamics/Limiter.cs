namespace M0DV0IC3.Dsp.Dynamics;

/// <summary>
/// Limitador de pico sin lookahead (latencia cero): ataque instantáneo y liberación suave.
/// Garantiza que la salida nunca supere el techo, aunque sumes voz y soundboard al máximo.
/// </summary>
public sealed class Limiter : IAudioEffect
{
    private readonly float _release;
    private readonly float _ceiling;
    private float _gain = 1f;

    public Limiter(int sampleRate, double ceilingDb = -1.0, double releaseMs = 80)
    {
        _ceiling = DspMath.DbToGain(ceilingDb);
        _release = DspMath.TimeConstant(releaseMs, sampleRate);
    }

    /// <summary>Ganancia que aplica ahora mismo: 1 = no está limitando.</summary>
    public float CurrentGain => _gain;

    public int LatencySamples => 0;

    public void Process(Span<float> buffer)
    {
        float ceiling = _ceiling;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            if (!float.IsFinite(x)) x = 0f;
            float a = Math.Abs(x);
            float target = a > ceiling ? ceiling / a : 1f;
            _gain = target < _gain ? target : target + _release * (_gain - target);
            buffer[i] = Math.Clamp(x * _gain, -ceiling, ceiling);
        }
    }

    public void Reset() => _gain = 1f;
}
