using M0DV0IC3.Dsp.Filters;

namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Vocoder de canales, el robot musical de la música electrónica. Un banco de 20 filtros (160 Hz-6,4 kHz) mide
/// cuánta energía tiene tu voz en cada banda, y esas envolventes dan forma a un acorde de dientes de sierra
/// (menor: tónica, tercera menor, quinta y octava). Las eses (por encima de 5 kHz) se mezclan de la voz original,
/// porque sin ellas no se entiende lo que dices. Solo filtros: no añade latencia.
/// </summary>
public sealed class Vocoder : IAudioEffect
{
    private const int Bands = 20;
    private const double LowestBandHz = 160;
    private const double HighestBandHz = 6400;
    private const float Makeup = 7f;

    private static readonly double[] ChordSemitones = [0, 3, 7, 12];

    private readonly int _sampleRate;
    private readonly Biquad[] _analysis = new Biquad[Bands];
    private readonly Biquad[] _synthesis = new Biquad[Bands];
    private readonly float[] _envelope = new float[Bands];
    private readonly double[] _phase = new double[ChordSemitones.Length];
    private readonly double[] _increment = new double[ChordSemitones.Length];
    private readonly Biquad _sibilance = new();
    private readonly float _attack;
    private readonly float _release;
    private uint _seed = 0x2545F491;
    private float _mix;

    public Vocoder(int sampleRate)
    {
        _sampleRate = sampleRate;
        double step = Math.Pow(HighestBandHz / LowestBandHz, 1.0 / (Bands - 1));
        // Ancho de cada banda: de la mitad del paso por debajo a la mitad por encima.
        double q = 1 / (Math.Sqrt(step) - 1 / Math.Sqrt(step));
        for (int b = 0; b < Bands; b++)
        {
            double hz = LowestBandHz * Math.Pow(step, b);
            _analysis[b] = new Biquad();
            _analysis[b].SetBandPass(sampleRate, hz, q);
            _synthesis[b] = new Biquad();
            _synthesis[b].SetBandPass(sampleRate, hz, q);
        }
        _sibilance.SetHighPass(sampleRate, 5000);
        _attack = DspMath.TimeConstant(2, sampleRate);
        _release = DspMath.TimeConstant(25, sampleRate);
        Configure(0, 110);
    }

    public bool IsActive => _mix > 0f;

    public int LatencySamples => 0;

    /// <param name="rootHz">Nota grave del acorde (110 Hz = La2).</param>
    public void Configure(double mix, double rootHz)
    {
        _mix = (float)Math.Clamp(mix, 0, 1);
        rootHz = Math.Clamp(rootHz > 0 ? rootHz : 110, 40, 500);
        for (int n = 0; n < ChordSemitones.Length; n++)
            _increment[n] = rootHz * DspMath.SemitonesToRatio(ChordSemitones[n]) / _sampleRate;
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        float dry = 1f - _mix;
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float carrier = 0f;
            for (int n = 0; n < _phase.Length; n++)
            {
                carrier += Saw(_phase[n], _increment[n]);
                _phase[n] += _increment[n];
                if (_phase[n] >= 1) _phase[n] -= 1;
            }
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            carrier = carrier * 0.25f + (_seed / 2147483648f - 1f) * 0.05f;

            float y = 0f;
            for (int b = 0; b < Bands; b++)
            {
                float a = Math.Abs(_analysis[b].Process(x));
                float e = _envelope[b];
                e = a + (a > e ? _attack : _release) * (e - a);
                _envelope[b] = e;
                y += _synthesis[b].Process(carrier) * e;
            }
            y = y * Makeup + 0.6f * _sibilance.Process(x);
            buffer[i] = dry * x + _mix * y;
        }
        for (int b = 0; b < Bands; b++) _envelope[b] = DspMath.FlushDenormal(_envelope[b]);
    }

    public void Reset()
    {
        Array.Clear(_envelope);
        Array.Clear(_phase);
    }

    /// <summary>Diente de sierra con PolyBLEP: sin el aliasing de un salto brusco.</summary>
    private static float Saw(double t, double dt)
    {
        double value = 2 * t - 1;
        if (t < dt)
        {
            double u = t / dt;
            value -= u + u - u * u - 1;
        }
        else if (t > 1 - dt)
        {
            double u = (t - 1) / dt;
            value -= u * u + u + u + 1;
        }
        return (float)value;
    }
}
