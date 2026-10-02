using M0DV0IC3.Dsp.Filters;

namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// "Aire": añade un soplo (ruido de 2,5-9 kHz) que sigue el volumen de la voz, como la aspiración de una voz
/// femenina o susurrada. Es proporcional a la voz, así que en silencio no añade nada. Latencia cero.
/// </summary>
public sealed class Breath : IAudioEffect
{
    /// <summary>Con <c>amount</c> = 1 el soplo queda unos 13 dB por debajo de la voz; con 0,35, unos 22 dB.</summary>
    private const float MaxLevel = 0.45f;

    private readonly Biquad _highPass = new();
    private readonly Biquad _lowPass = new();
    private readonly float _attack;
    private readonly float _release;
    private uint _seed = 0x9E3779B9;
    private float _envelope;
    private float _amount;

    public Breath(int sampleRate)
    {
        _highPass.SetHighPass(sampleRate, 2500);
        _lowPass.SetLowPass(sampleRate, 9000);
        _attack = DspMath.TimeConstant(3, sampleRate);
        _release = DspMath.TimeConstant(40, sampleRate);
    }

    public bool IsActive => _amount > 0f;

    public int LatencySamples => 0;

    public void Configure(double amount) => _amount = (float)Math.Clamp(amount, 0, 1);

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        float level = _amount * MaxLevel;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float a = Math.Abs(x);
            _envelope = a + (a > _envelope ? _attack : _release) * (_envelope - a);

            // xorshift32: ruido blanco en [-1, 1) sin reservar memoria.
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            float noise = _seed / 2147483648f - 1f;

            buffer[i] = x + level * _envelope * _lowPass.Process(_highPass.Process(noise));
        }
        _envelope = DspMath.FlushDenormal(_envelope);
    }

    public void Reset()
    {
        _envelope = 0;
        _highPass.Reset();
        _lowPass.Reset();
    }
}
