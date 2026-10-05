using M0DV0IC3.Dsp.Filters;

namespace M0DV0IC3.Dsp.Effects;

public enum TransmissionStyle
{
    Off,

    /// <summary>Walkie-talkie: chasquido de ruido al empezar a hablar y pitido de fin («roger beep») al acabar.</summary>
    Walkie,

    /// <summary>Astronauta: los dos tonos de la NASA (Quindar), uno al empezar y otro al acabar.</summary>
    Space,
}

/// <summary>
/// Sonidos de una transmisión por radio alrededor de lo que dices: ruido de fondo mientras hablas y los pitidos
/// de inicio y fin. Detecta cuándo hablas con una envolvente (con 350 ms de margen para las pausas entre
/// palabras), así que en silencio no añade nada. Sin latencia: el pitido de inicio suena con la primera sílaba.
/// </summary>
public sealed class Transmission : IAudioEffect
{
    private const float OpenLevel = 0.01f;    // -40 dBFS
    private const float CloseLevel = 0.005f;  // -46 dBFS

    private readonly int _sampleRate;
    private readonly int _hangover;
    private readonly float _envelopeRelease;
    private readonly Biquad _noiseHighPass = new();
    private readonly Biquad _noiseLowPass = new();
    private TransmissionStyle _style;
    private float _amount;
    private float _envelope;
    private bool _talking;
    private int _silence;
    private uint _seed = 0x6C8E9CF5;

    // Sonido de inicio o fin que está sonando: tono (0 = ruido), muestras que quedan y su duración total.
    private double _toneHz;
    private double _tonePhase;
    private int _cueLeft;
    private int _cueLength;
    private float _cueLevel;
    private int _tailLeft;

    public Transmission(int sampleRate)
    {
        _sampleRate = sampleRate;
        _hangover = sampleRate * 350 / 1000;
        _envelopeRelease = DspMath.TimeConstant(40, sampleRate);
        _noiseHighPass.SetHighPass(sampleRate, 600);
        _noiseLowPass.SetLowPass(sampleRate, 3200);
    }

    public bool IsActive => _style != TransmissionStyle.Off;

    public int LatencySamples => 0;

    /// <param name="amount">Volumen del ruido y de los pitidos, 0..1.</param>
    public void Configure(TransmissionStyle style, double amount)
    {
        _style = style;
        _amount = (float)Math.Clamp(amount, 0, 1);
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        float hiss = (_style == TransmissionStyle.Walkie ? 0.03f : 0.015f) * _amount;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float a = Math.Abs(x);
            _envelope = a > _envelope ? a : a + _envelopeRelease * (_envelope - a);

            if (!_talking && _envelope > OpenLevel)
            {
                _talking = true;
                _silence = 0;
                StartCue(begin: true);
            }
            else if (_talking)
            {
                _silence = _envelope < CloseLevel ? _silence + 1 : 0;
                if (_silence > _hangover)
                {
                    _talking = false;
                    StartCue(begin: false);
                }
            }

            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            float noise = _noiseLowPass.Process(_noiseHighPass.Process(_seed / 2147483648f - 1f));

            float y = x;
            if (_talking) y += hiss * noise;
            if (_cueLeft > 0)
            {
                float fade = Math.Min(1f, Math.Min(_cueLeft, _cueLength - _cueLeft) / (_sampleRate * 0.004f));
                float cue = _toneHz > 0 ? (float)Math.Sin(_tonePhase) : noise * 3f;
                _tonePhase += 2 * Math.PI * _toneHz / _sampleRate;
                if (_tonePhase > 2 * Math.PI) _tonePhase -= 2 * Math.PI;
                y += _cueLevel * fade * cue;
                if (--_cueLeft == 0 && _tailLeft > 0)
                {
                    // Tras el pitido de fin del walkie, la cola de ruido de cuando se suelta el botón.
                    _toneHz = 0;
                    _cueLength = _cueLeft = _tailLeft;
                    _cueLevel = 0.05f * _amount;
                    _tailLeft = 0;
                }
            }
            buffer[i] = y;
        }
        _envelope = DspMath.FlushDenormal(_envelope);
    }

    public void Reset()
    {
        _envelope = 0;
        _talking = false;
        _cueLeft = _tailLeft = 0;
    }

    private void StartCue(bool begin)
    {
        if (_style == TransmissionStyle.Space)
        {
            // Tonos Quindar: 2525 Hz al empezar, 2475 Hz al acabar, de 250 ms.
            Cue(begin ? 2525 : 2475, 0.25, 0.1f);
        }
        else if (begin)
        {
            Cue(0, 0.07, 0.06f); // chasquido de ruido al apretar el botón
        }
        else
        {
            Cue(1250, 0.12, 0.14f); // «roger beep»
            _tailLeft = _sampleRate * 160 / 1000;
        }
    }

    private void Cue(double hz, double seconds, float level)
    {
        _toneHz = hz;
        _tonePhase = 0;
        _cueLength = _cueLeft = (int)(seconds * _sampleRate);
        _cueLevel = level * _amount;
        _tailLeft = 0;
    }
}
