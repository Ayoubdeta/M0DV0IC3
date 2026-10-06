using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>
/// Cambia la voz del cantante de la canción que suena: separa su voz con el filtro del karaoke
/// (<see cref="VocalRemover.Split"/>), le pone una voz con su propia cadena y la vuelve a mezclar con el resto.
/// <para>Medido con 140 canciones (MUSDB18, pistas separadas): ~80 % de la voz va a la parte que se cambia. Con ella va
/// la música que suena en el centro, ~2 dB por debajo de la voz, y la voz original que se queda sin cambiar suena
/// ~4,5 dB por debajo de la música. El bajo casi no se toca (-16 dB).</para>
/// <para>La voz tiene su propio tono medio (el del cantante), así que las voces con tono objetivo (Mujer, Grave...)
/// cambian lo justo para cada canción. El resto espera lo que tarda la voz, para que el cantante no vaya por detrás.</para>
/// Entra estéreo y sale mono. No reserva memoria al procesar.
/// </summary>
public sealed class SongVoiceChanger
{
    private const int MaxDelay = 1 << 14;

    private readonly VocalRemover _separator = new() { RestoreLoudness = false };
    private readonly float[] _rest;
    private readonly float[] _vocals;
    private readonly float[] _delay = new float[MaxDelay];
    private int _delayPos;

    public SongVoiceChanger(int maxBlockSize = 8192)
    {
        _rest = new float[maxBlockSize];
        _vocals = new float[maxBlockSize];
        Voice = new VoiceProcessor(DspMath.SampleRate, maxBlockSize);
    }

    /// <summary>La voz que se le pone al cantante (se cambia con <see cref="VoiceProcessor.SetPreset"/>).</summary>
    public VoiceProcessor Voice { get; }

    public int LatencySamples => VocalRemover.FrameSize + Voice.LatencySamples;

    public void Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> mono)
    {
        if (left.Length > _rest.Length) throw new ArgumentException("Bloque demasiado grande", nameof(left));
        var rest = _rest.AsSpan(0, left.Length);
        var vocals = _vocals.AsSpan(0, left.Length);
        _separator.Split(left, right, rest, vocals);
        Voice.Process(vocals);

        int latency = Math.Min(Voice.LatencySamples, MaxDelay - 1);
        for (int i = 0; i < mono.Length; i++)
        {
            _delay[_delayPos] = rest[i];
            mono[i] = _delay[(_delayPos - latency) & (MaxDelay - 1)] + vocals[i];
            _delayPos = (_delayPos + 1) & (MaxDelay - 1);
        }
    }

    public void Reset()
    {
        _separator.Reset();
        Voice.Reset();
        Array.Clear(_delay);
    }
}
