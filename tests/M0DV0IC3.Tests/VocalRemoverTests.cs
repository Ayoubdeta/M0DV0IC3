using M0DV0IC3.Audio.AppAudio;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Filters;
using M0DV0IC3.Dsp.Presets;
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

    // Donde se entienden las palabras: de 400 Hz a 4 kHz (la fundamental de una voz grave y los agudos de las "s" se
    // dejan a propósito).
    private static double VoiceBandDb(float[] x)
    {
        var y = (float[])x.Clone();
        var highPass = new Biquad();
        var lowPass = new Biquad();
        highPass.SetHighPass(Rate, 400);
        lowPass.SetLowPass(Rate, 4000);
        for (int pass = 0; pass < 2; pass++)
        {
            highPass.Reset();
            lowPass.Reset();
            highPass.Process(y);
            lowPass.Process(y);
        }
        return RmsDb(y);
    }

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
        var voice = TestSignals.Vowel(130, 1.5);
        double before = VoiceBandDb(Run(voice, voice, 0));
        double after = VoiceBandDb(Run(voice, voice, 1));
        output.WriteLine($"voz grave en el centro, de 400 Hz a 4 kHz: {before:F1} dB → {after:F1} dB");
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

    // Sin la voz la canción baja de volumen; poco a poco se recupera (hasta +6 dB) para que el ritmo no se quede bajo.
    [Fact]
    public void The_song_gets_its_loudness_back()
    {
        var voice = TestSignals.Vowel(280, 6, 0.4f);
        var guitar = TestSignals.Sawtooth(415, 6, 0.1f);
        var left = new float[voice.Length];
        for (int i = 0; i < left.Length; i++) left[i] = voice[i] + guitar[i];
        var original = Run(left, voice, 0);
        var karaoke = Run(left, voice, 1);
        double before = DspMath.GainToDb(DspMath.Rms(original.AsSpan(5 * Rate)));
        double after = DspMath.GainToDb(DspMath.Rms(karaoke.AsSpan(5 * Rate)));
        double guitarAlone = DspMath.GainToDb(DspMath.Rms(Run(guitar, new float[guitar.Length], 0).AsSpan(5 * Rate)));
        output.WriteLine($"canción: {before:F1} dB, sin voz: {after:F1} dB (la guitarra sola: {guitarAlone:F1} dB)");
        Assert.InRange(after - guitarAlone, 4, 6.5);
        Assert.True(after < before + 0.5);
    }

    [Fact]
    public void Split_parts_add_up_to_the_original_song()
    {
        var voice = TestSignals.Vowel(220, 1.5, 0.3f);
        var guitar = TestSignals.Sawtooth(330, 1.5, 0.2f);
        var left = new float[voice.Length];
        for (int i = 0; i < left.Length; i++) left[i] = voice[i] + guitar[i];
        var remover = new VocalRemover { RestoreLoudness = false };
        var rest = new float[voice.Length];
        var vocals = new float[voice.Length];
        for (int offset = 0; offset < left.Length; offset += 480)
        {
            int n = Math.Min(480, left.Length - offset);
            remover.Split(left.AsSpan(offset, n), voice.AsSpan(offset, n), rest.AsSpan(offset, n), vocals.AsSpan(offset, n));
        }
        int delay = VocalRemover.FrameSize;
        for (int i = delay; i < left.Length; i++)
            Assert.Equal((left[i - delay] + voice[i - delay]) / 2, rest[i] + vocals[i], 1e-5);

        // La voz del centro va a la parte "voz"; la guitarra, que solo suena a la izquierda, se queda en el resto.
        double voiceInVocals = VoiceBandDb(vocals) - VoiceBandDb(Run(voice, voice, 0));
        double guitarInRest = RmsDb(rest) - RmsDb(Run(guitar, new float[guitar.Length], 0));
        output.WriteLine($"voz separada: {voiceInVocals:+0.0;-0.0} dB; resto frente a la guitarra sola: {guitarInRest:+0.0;-0.0} dB");
        Assert.InRange(voiceInVocals, -1.5, 0.5);
        Assert.InRange(guitarInRest, -1.5, 1.5);
    }

    [Fact]
    public void The_singer_gets_the_new_voice_and_the_side_instruments_stay()
    {
        var changer = new SongVoiceChanger();
        changer.Voice.SetPreset(VoicePreset.Neutral with { Id = "octava", PitchSemitones = 12 });
        var singer = TestSignals.Vowel(180, 2.0, 0.3f);
        var output1 = RunChanger(changer, singer, singer);
        double f0 = TestSignals.MeasureF0(TestSignals.Segment(output1, 1.0, 1.8));
        output.WriteLine($"cantante a 180 Hz con +12 semitonos: {f0:F1} Hz");
        Assert.InRange(f0, 349, 371);

        changer.Reset();
        var guitar = TestSignals.Sawtooth(330, 2.0, 0.2f);
        var silence = new float[guitar.Length];
        var output2 = RunChanger(changer, guitar, silence);
        double change = DspMath.GainToDb(DspMath.Rms(TestSignals.Segment(output2, 1.0, 1.8)))
            - DspMath.GainToDb(DspMath.Rms(TestSignals.Segment(guitar, 1.0, 1.8)) / 2);
        output.WriteLine($"guitarra a la izquierda: {change:+0.0;-0.0} dB");
        Assert.InRange(change, -1, 1);
        Assert.InRange(TestSignals.MeasureF0(TestSignals.Segment(output2, 1.0, 1.8)), 327, 333);
    }

    [Fact]
    public void Changing_the_singer_does_not_allocate()
    {
        var changer = new SongVoiceChanger();
        changer.Voice.SetPreset(VoicePreset.Neutral with { Id = "tono", PitchSemitones = 5 });
        var left = TestSignals.Noise(0.5, 0.2f, seed: 1);
        var right = TestSignals.Noise(0.5, 0.2f, seed: 2);
        var mono = new float[480];
        changer.Process(left.AsSpan(0, 480), right.AsSpan(0, 480), mono);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int offset = 480; offset + 480 <= left.Length; offset += 480)
            changer.Process(left.AsSpan(offset, 480), right.AsSpan(offset, 480), mono);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static float[] RunChanger(SongVoiceChanger changer, float[] left, float[] right)
    {
        var mono = new float[left.Length];
        for (int offset = 0; offset < left.Length; offset += 480)
        {
            int n = Math.Min(480, left.Length - offset);
            changer.Process(left.AsSpan(offset, n), right.AsSpan(offset, n), mono.AsSpan(offset, n));
        }
        return mono;
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
