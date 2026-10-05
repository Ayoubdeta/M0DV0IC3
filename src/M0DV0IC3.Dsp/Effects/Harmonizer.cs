using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Coro: tu voz y tres copias con otro tono a la vez (una octava abajo, una quinta y una octava arriba), cada una
/// con su propio PSOLA. Las copias llegan unos 15-25 ms después que la voz, como las voces de un coro real; la
/// voz principal no se retrasa. Cuesta como tres voces con cambio de tono.
/// </summary>
public sealed class Harmonizer : IAudioEffect
{
    private const int ChunkSize = 1024;

    // Semitonos y nivel de cada copia. Con un poco de desafinación (centésimas) no suenan a eco metálico.
    private static readonly (double Semitones, float Gain)[] Voices = [(-12.06, 0.6f), (7.04, 0.42f), (11.95, 0.32f)];

    private readonly PsolaPitchShifter[] _shifters;
    private readonly float[] _scratch = new float[ChunkSize];
    private readonly float[] _sum = new float[ChunkSize];
    private float _mix;

    public Harmonizer(int sampleRate, VoiceRange range)
    {
        _shifters = new PsolaPitchShifter[Voices.Length];
        for (int v = 0; v < Voices.Length; v++)
        {
            _shifters[v] = new PsolaPitchShifter(sampleRate, range);
            _shifters[v].SetParameters(DspMath.SemitonesToRatio(Voices[v].Semitones), 1.0, 0);
        }
    }

    public bool IsActive => _mix > 0f;

    public int LatencySamples => 0;

    public void Configure(double mix) => _mix = (float)Math.Clamp(mix, 0, 1);

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        // La voz baja un poco con mucha mezcla, para que el coro no suene mucho más fuerte que la voz sola.
        float dry = 1f - 0.3f * _mix;
        for (int offset = 0; offset < buffer.Length; offset += ChunkSize)
        {
            var block = buffer.Slice(offset, Math.Min(ChunkSize, buffer.Length - offset));
            var sum = _sum.AsSpan(0, block.Length);
            var scratch = _scratch.AsSpan(0, block.Length);
            sum.Clear();
            for (int v = 0; v < _shifters.Length; v++)
            {
                block.CopyTo(scratch);
                _shifters[v].Process(scratch);
                float gain = Voices[v].Gain * _mix;
                for (int i = 0; i < block.Length; i++) sum[i] += gain * scratch[i];
            }
            for (int i = 0; i < block.Length; i++) block[i] = dry * block[i] + sum[i];
        }
    }

    public void Reset()
    {
        foreach (var shifter in _shifters) shifter.Reset();
    }
}
