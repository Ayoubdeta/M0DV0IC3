using M0DV0IC3.Dsp;

namespace M0DV0IC3.Audio.Soundboard;

/// <summary>Sonido ya decodificado en memoria (mono, 48 kHz), listo para reproducirse sin tocar el disco.</summary>
public sealed class SoundClip
{
    public SoundClip(string id, string name, float[] samples)
    {
        Id = id;
        Name = name;
        Samples = samples;
    }

    public string Id { get; }

    public string Name { get; }

    public float[] Samples { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / DspMath.SampleRate);
}
