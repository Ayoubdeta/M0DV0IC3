using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Dsp;

namespace M0DV0IC3.Tests;

public sealed class SoundTrimTests
{
    private const int Rate = TestSignals.Rate;

    private static SoundClip Clip(params float[][] parts) => new("t", "t", parts.SelectMany(p => p).ToArray());

    [Fact]
    public void Untrimmed_clip_is_reused()
    {
        var full = Clip(TestSignals.Sine(440, 1.0));
        Assert.Same(full, SoundTrim.Apply(full, 0, 0));
        Assert.Same(full, SoundTrim.Apply(full, 0, 5));
    }

    [Fact]
    public void Trim_keeps_the_chosen_part_with_short_fades()
    {
        var full = Clip(TestSignals.Sine(440, 2.0));
        var trimmed = SoundTrim.Apply(full, 0.5, 1.25);

        Assert.Equal((int)(0.75 * Rate), trimmed.Samples.Length);
        // Empieza y acaba en silencio (fundido de 5 ms) y por dentro es el sonido original.
        Assert.True(Math.Abs(trimmed.Samples[0]) < 1e-6f);
        Assert.True(Math.Abs(trimmed.Samples[^1]) < 0.01f);
        int middle = trimmed.Samples.Length / 2;
        Assert.Equal(full.Samples[Rate / 2 + middle], trimmed.Samples[middle]);
        Assert.Equal(1.0, SoundTrim.Apply(full, 1.0, 0).Duration.TotalSeconds, 3);
    }

    [Fact]
    public void Silence_at_both_ends_is_detected()
    {
        var full = Clip(new float[Rate / 2], TestSignals.Sine(300, 1.0), new float[Rate]);
        var (start, end) = SoundTrim.DetectSound(full.Samples);
        Assert.InRange(start, 0.45, 0.5);
        Assert.InRange(end, 1.5, 1.55);
    }

    [Fact]
    public void Peaks_follow_the_loudness()
    {
        var full = Clip(TestSignals.Sine(300, 1.0, amplitude: 0.1f), TestSignals.Sine(300, 1.0, amplitude: 0.8f));
        var peaks = SoundTrim.Peaks(full.Samples, 10);
        Assert.Equal(10, peaks.Length);
        Assert.InRange(peaks[2], 0.09f, 0.11f);
        Assert.InRange(peaks[7], 0.79f, 0.81f);
    }
}
