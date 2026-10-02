namespace M0DV0IC3.Dsp.Dynamics;

/// <summary>
/// Puerta de ruido sin lookahead (no añade latencia). Tiene histéresis de 6 dB, un hold de 80 ms
/// y rampas de ganancia, para que no corte el final de las palabras ni haga clics.
/// </summary>
public sealed class NoiseGate : IAudioEffect
{
    public const double DisabledThresholdDb = -90;

    private const double HysteresisDb = 6;
    private const double HoldMs = 80;

    private readonly float _envelopeRelease;
    private readonly float _gainAttack;
    private readonly float _gainRelease;
    private readonly int _holdSamples;

    private float _openThreshold;
    private float _closeThreshold;
    private float _envelope;
    private float _gain = 1f;
    private int _holdCounter;
    private bool _open;

    public NoiseGate(int sampleRate, double thresholdDb = -50)
    {
        _envelopeRelease = DspMath.TimeConstant(30, sampleRate);
        _gainAttack = DspMath.TimeConstant(1, sampleRate);
        _gainRelease = DspMath.TimeConstant(60, sampleRate);
        _holdSamples = (int)(HoldMs * sampleRate / 1000);
        ThresholdDb = thresholdDb;
    }

    /// <summary>Nivel en dBFS por encima del cual se abre la puerta. -90 o menos la desactiva.</summary>
    public double ThresholdDb
    {
        get => DspMath.GainToDb(_openThreshold);
        set
        {
            _openThreshold = DspMath.DbToGain(value);
            _closeThreshold = DspMath.DbToGain(value - HysteresisDb);
        }
    }

    public bool IsActive => DspMath.GainToDb(_openThreshold) > DisabledThresholdDb;

    public bool IsOpen => _open;

    public int LatencySamples => 0;

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float a = Math.Abs(x);
            _envelope = a > _envelope ? a : a + _envelopeRelease * (_envelope - a);

            if (_envelope >= _openThreshold)
            {
                _open = true;
                _holdCounter = _holdSamples;
            }
            else if (_envelope < _closeThreshold)
            {
                if (_holdCounter > 0) _holdCounter--;
                else _open = false;
            }

            float target = _open ? 1f : 0f;
            float coefficient = target > _gain ? _gainAttack : _gainRelease;
            _gain = target + coefficient * (_gain - target);
            buffer[i] = x * _gain;
        }
        _envelope = DspMath.FlushDenormal(_envelope);
        _gain = DspMath.FlushDenormal(_gain);
    }

    public void Reset()
    {
        _envelope = 0;
        _gain = 1f;
        _holdCounter = 0;
        _open = false;
    }
}
