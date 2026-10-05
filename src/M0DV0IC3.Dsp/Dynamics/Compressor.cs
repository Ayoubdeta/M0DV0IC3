namespace M0DV0IC3.Dsp.Dynamics;

/// <summary>
/// Compresor sin lookahead (no añade latencia): acerca el volumen de lo que dices flojo y lo que dices fuerte,
/// como en la radio. Un solo mando: con <c>amount</c> = 1, umbral -36 dBFS y relación 8:1. La ganancia de
/// compensación devuelve la voz a un nivel parecido al de entrada.
/// </summary>
public sealed class Compressor : IAudioEffect
{
    private readonly float _attack;
    private readonly float _release;
    private float _envelope;
    private float _thresholdDb;
    private float _slope;
    private float _makeupDb;
    private bool _active;

    public Compressor(int sampleRate)
    {
        _attack = DspMath.TimeConstant(4, sampleRate);
        _release = DspMath.TimeConstant(150, sampleRate);
    }

    public bool IsActive => _active;

    public int LatencySamples => 0;

    public void Configure(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        _active = amount > 0.005;
        _thresholdDb = (float)(-10 - 26 * amount);
        double ratio = 1 + 7 * amount;
        _slope = (float)(1 - 1 / ratio);
        // La mitad de lo que se reduciría a -6 dBFS: la voz normal queda más o menos igual de alta.
        _makeupDb = (float)(Math.Max(0, -6 - _thresholdDb) * _slope * 0.5);
    }

    public void Process(Span<float> buffer)
    {
        if (!_active) return;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float a = Math.Abs(x);
            _envelope = a + (a > _envelope ? _attack : _release) * (_envelope - a);
            float levelDb = 20f * MathF.Log10(_envelope + 1e-9f);
            float reductionDb = levelDb > _thresholdDb ? (levelDb - _thresholdDb) * _slope : 0f;
            buffer[i] = x * MathF.Pow(10f, (_makeupDb - reductionDb) / 20f);
        }
        _envelope = DspMath.FlushDenormal(_envelope);
    }

    public void Reset() => _envelope = 0f;
}
