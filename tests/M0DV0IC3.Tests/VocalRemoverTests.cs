using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Filters;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class VocalRemoverTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    private static float[] Run(float[] left, float[] right, float strength)
    {
        var remover = new VocalRemover { Strength = strength };
        var mono = new float[left.Length];
        for (int offset = 0; offset < left.Length; offset += 480)
        {
            int n = Math.Min(480, left.Length - offset);
            remover.Process(left.AsSpan(offset, n), right.AsSpan(offset, n), mono.AsSpan(offset, n));
        }
        return mono;
    }

    private static double RmsDb(float[] x) => DspMath.GainToDb(DspMath.Rms(x.AsSpan(Rate / 2)));

    [Fact]
    public void Fft_roundtrip_is_exact()
    {
        var fft = new Fft(256);
        var re = new double[256];
        var im = new double[256];
        var random = new Random(3);
        for (int i = 0; i < 256; i++) re[i] = random.NextDouble() - 0.5;
        var original = (double[])re.Clone();
        fft.Forward(re, im);
        fft.Inverse(re, im);
        for (int i = 0; i < 256; i++) Assert.Equal(original[i], re[i], 9);
    }

    [Fact]
    public void Without_strength_the_song_comes_out_intact_but_delayed()
    {
        var left = TestSignals.Vowel(180, 1.0);
        var right = TestSignals.Noise(1.0, 0.2f);
        var mono = Run(left, right, 0);
        int delay = VocalRemover.FrameSize;
        for (int i = delay; i < left.Length; i++)
            Assert.Equal((left[i - delay] + right[i - delay]) / 2, mono[i], 4);
    }

    [Fact]
    public void Centered_voice_is_removed()
    {
        var voice = TestSignals.Vowel(200, 1.5);
        double before = RmsDb(Run(voice, voice, 0));
        double after = RmsDb(Run(voice, voice, 1));
        output.WriteLine($"voz en el centro: {before:F1} dB → {after:F1} dB");
        Assert.True(after < before - 15);
    }

    [Fact]
    public void Instruments_on_one_side_and_the_bass_stay()
    {
        var guitar = TestSignals.Sawtooth(330, 1.5, 0.3f);
        var silence = new float[guitar.Length];
        double side = RmsDb(Run(guitar, silence, 1)) - RmsDb(Run(guitar, silence, 0));

        var bass = TestSignals.Sine(55, 1.5, 0.4f);
        double low = RmsDb(Run(bass, bass, 1)) - RmsDb(Run(bass, bass, 0));
        output.WriteLine($"guitarra a la izquierda: {side:+0.0;-0.0} dB, bajo en el centro: {low:+0.0;-0.0} dB");
        Assert.InRange(side, -1, 0.5);
        Assert.InRange(low, -1.5, 0.5);
    }

    [Fact]
    public void Processing_does_not_allocate()
    {
        var remover = new VocalRemover();
        var left = TestSignals.Noise(0.5, 0.2f, seed: 1);
        var right = TestSignals.Noise(0.5, 0.2f, seed: 2);
        var mono = new float[480];
        remover.Process(left.AsSpan(0, 480), right.AsSpan(0, 480), mono);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int offset = 480; offset + 480 <= left.Length; offset += 480)
            remover.Process(left.AsSpan(offset, 480), right.AsSpan(offset, 480), mono);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
