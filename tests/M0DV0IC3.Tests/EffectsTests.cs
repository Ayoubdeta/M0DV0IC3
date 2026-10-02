using System.Text.Json;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Filters;
using M0DV0IC3.Dsp.Presets;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class EffectsTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    public static TheoryData<string> VoiceIds()
    {
        var data = new TheoryData<string>();
        foreach (var voice in BuiltInVoices.All) data.Add(voice.Id);
        return data;
    }

    [Fact]
    public void Biquad_lowpass_passes_lows_and_cuts_highs()
    {
        var low = TestSignals.Sine(100, 0.5);
        var high = TestSignals.Sine(10000, 0.5);
        var filter = new Biquad();
        filter.SetLowPass(Rate, 1000);
        filter.Process(low);
        filter.Reset();
        filter.Process(high);

        Assert.InRange(DspMath.GainToDb(DspMath.Rms(low.AsSpan(Rate / 4)) / (0.5 / Math.Sqrt(2))), -1, 0.5);
        Assert.True(DspMath.GainToDb(DspMath.Rms(high.AsSpan(Rate / 4)) / (0.5 / Math.Sqrt(2))) < -30);
    }

    [Fact]
    public void Limiter_never_exceeds_ceiling()
    {
        var x = TestSignals.Sine(300, 0.5, amplitude: 4f);
        x[1000] = float.NaN;
        x[2000] = float.PositiveInfinity;
        new Limiter(Rate, ceilingDb: -1).Process(x);
        Assert.All(x, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= DspMath.DbToGain(-1) + 1e-6f));
    }

    [Fact]
    public void Noise_gate_closes_on_background_noise_and_opens_on_voice()
    {
        var gate = new NoiseGate(Rate, thresholdDb: -45);
        var hiss = TestSignals.Noise(0.5, DspMath.DbToGain(-65));
        gate.Process(hiss);
        Assert.True(DspMath.Rms(hiss.AsSpan(Rate / 4)) < DspMath.DbToGain(-80));

        var voice = TestSignals.Vowel(150, 0.5, amplitude: 0.3f);
        var original = (float[])voice.Clone();
        gate.Process(voice);
        Assert.InRange(DspMath.Rms(voice.AsSpan(Rate / 10)) / DspMath.Rms(original.AsSpan(Rate / 10)), 0.95, 1.01);
    }

    [Theory]
    [MemberData(nameof(VoiceIds))]
    public void Every_voice_produces_finite_bounded_output(string id)
    {
        var preset = BuiltInVoices.Find(id)!;
        var chain = new VoiceChain(preset);
        var input = TestSignals.Vowel(140, 2.0, amplitude: 0.9f, vibratoHz: 5);
        var noise = TestSignals.Noise(2.0, 0.3f);
        for (int i = Rate; i < input.Length; i++) input[i] = noise[i];

        var result = TestSignals.ProcessInBlocks(chain, input);
        float peak = DspMath.Peak(result);
        output.WriteLine($"{id}: pico {DspMath.GainToDb(peak):F1} dBFS, rms {DspMath.GainToDb(DspMath.Rms(result)):F1} dBFS, latencia {chain.LatencySamples * 1000.0 / Rate:F1} ms");
        Assert.All(result, v => Assert.True(float.IsFinite(v)));
        Assert.True(peak < 4f, $"pico excesivo antes del limitador: {peak}");
        Assert.True(DspMath.Rms(result) > DspMath.DbToGain(-40), "la voz no debería quedar casi muda");
    }

    [Fact]
    public void Breath_adds_soft_high_noise_that_follows_the_voice()
    {
        var breath = new Breath(Rate);
        breath.Configure(0.35);

        var silence = new float[Rate / 2];
        breath.Process(silence);
        Assert.All(silence, v => Assert.Equal(0f, v));

        var voice = TestSignals.Sine(300, 1.0, amplitude: 0.3f);
        var original = (float[])voice.Clone();
        breath.Process(voice);
        var added = new float[voice.Length];
        for (int i = 0; i < voice.Length; i++) added[i] = voice[i] - original[i];

        double relativeDb = DspMath.GainToDb(DspMath.Rms(added.AsSpan(Rate / 2)) / DspMath.Rms(original.AsSpan(Rate / 2)));
        double centroid = TestSignals.SpectralCentroid(added.AsSpan(Rate / 2));
        output.WriteLine($"aire: {relativeDb:F1} dB respecto a la voz, centroide {centroid:F0} Hz");
        Assert.InRange(relativeDb, -28, -16);
        Assert.InRange(centroid, 3000, 9000);
    }

    [Fact]
    public void Mujer2_is_higher_and_brighter_than_mujer()
    {
        var input = TestSignals.Vowel(120, 1.5, vibratoHz: 5);
        var mujer = TestSignals.ProcessInBlocks(new VoiceChain(BuiltInVoices.Find("mujer")!), input);
        var mujer2 = TestSignals.ProcessInBlocks(new VoiceChain(BuiltInVoices.Find("mujer2")!), input);

        // Rango de voz real: con los graves recortados y el aire, el formante de ~800 Hz engañaría a la autocorrelación.
        double f0 = TestSignals.MeasureF0(TestSignals.Segment(mujer2, 0.6, 1.4), 80, 400);
        double brightness1 = TestSignals.SpectralCentroid(TestSignals.Segment(mujer, 0.6, 1.4));
        double brightness2 = TestSignals.SpectralCentroid(TestSignals.Segment(mujer2, 0.6, 1.4));
        output.WriteLine($"mujer2: f0 {f0:F1} Hz (esperado {120 * DspMath.SemitonesToRatio(8):F1}), centroide {brightness1:F0} → {brightness2:F0} Hz");

        Assert.InRange(f0, 120 * DspMath.SemitonesToRatio(8) * 0.97, 120 * DspMath.SemitonesToRatio(8) * 1.03);
        Assert.True(brightness2 > brightness1);
    }

    [Fact]
    public void Voices_only_add_latency_where_expected()
    {
        foreach (var preset in BuiltInVoices.All.Where(v => !v.UsesPitch))
        {
            int latency = new VoiceChain(preset).LatencySamples;
            if (preset.UsesReverse) Assert.Equal((int)(preset.ReverseMs * Rate / 1000), latency);
            else if (preset.VibratoSemitones > 0) Assert.InRange(latency, 1, Rate * 2 / 1000);
            else Assert.Equal(0, latency);
        }
    }

    [Fact]
    public void Whisper_removes_the_pitch_but_keeps_level_and_formants()
    {
        var whisper = new Whisper(Rate);
        whisper.Configure(1);
        var input = TestSignals.Vowel(150, 1.5);
        var result = TestSignals.ProcessInBlocks(whisper, input);

        var dry = TestSignals.Segment(input, 0.5, 1.4);
        var wet = TestSignals.Segment(result, 0.5, 1.4);
        double levelDb = DspMath.GainToDb(DspMath.Rms(wet) / DspMath.Rms(dry));
        double periodicityIn = TestSignals.Periodicity(dry, Rate / 150);
        double periodicityOut = TestSignals.Periodicity(wet, Rate / 150);
        double centroidIn = TestSignals.SpectralCentroid(dry);
        double centroidOut = TestSignals.SpectralCentroid(wet);
        output.WriteLine($"susurro: nivel {levelDb:+0.0;-0.0} dB, periodicidad {periodicityIn:F2} → {periodicityOut:F2}, centroide {centroidIn:F0} → {centroidOut:F0} Hz");

        Assert.All(result, v => Assert.True(float.IsFinite(v)));
        Assert.InRange(levelDb, -3, 3);
        Assert.True(periodicityIn > 0.8);
        Assert.True(periodicityOut < 0.3, "el susurro no debe tener tono");
        Assert.InRange(centroidOut / centroidIn, 0.6, 1.6);
    }

    [Fact]
    public void Reverse_plays_each_fragment_backwards()
    {
        const int fragment = Rate / 10;
        var reverse = new Reverse(Rate);
        reverse.Configure(100);
        Assert.Equal(fragment, reverse.LatencySamples);

        var input = TestSignals.Noise(1.0, 0.5f);
        var result = TestSignals.ProcessInBlocks(reverse, input);

        // En el centro de cada fragmento solo suena el lector A, que lee hacia atrás desde n - L - 1.
        for (int k = 2; k < 9; k++)
        {
            int center = k * fragment + fragment / 2;
            for (int d = -100; d <= 100; d += 10)
                Assert.Equal(input[center - fragment - 1 - d], result[center + d], 0.01f);
        }

        // Las dos ventanas suman 1: una señal constante sale constante.
        var dcReverse = new Reverse(Rate);
        dcReverse.Configure(100);
        var dc = TestSignals.ProcessInBlocks(dcReverse, Enumerable.Repeat(0.5f, Rate).ToArray());
        Assert.All(dc[(2 * fragment)..], v => Assert.Equal(0.5f, v, 1e-4f));
    }

    [Fact]
    public void Flanger_sweeps_resonances_through_a_steady_tone()
    {
        var flanger = new Flanger(Rate);
        flanger.Configure(0.8, 0.25);
        var result = TestSignals.ProcessInBlocks(flanger, TestSignals.Sine(1000, 4.5, 0.3f));

        double min = double.MaxValue, max = 0;
        for (double t = 0.25; t + 0.05 <= 4.25; t += 0.05)
        {
            double rms = DspMath.Rms(TestSignals.Segment(result, t, t + 0.05));
            min = Math.Min(min, rms);
            max = Math.Max(max, rms);
        }
        output.WriteLine($"flanger: nivel entre {DspMath.GainToDb(min / 0.212):F1} y {DspMath.GainToDb(max / 0.212):F1} dB");
        Assert.True(max / min > 2.5, "el barrido debe crear picos y valles");
        Assert.Equal(0, flanger.LatencySamples);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Vibrato_wobbles_the_pitch_by_the_requested_depth(bool withPsola)
    {
        // Sin PSOLA es un retardo modulado; con PSOLA (aquí, +0,5 semitonos) se hace en los granos.
        var preset = VoicePreset.Neutral with { Id = "test", VibratoHz = 5, VibratoSemitones = 1, PitchSemitones = withPsola ? 0.5 : 0 };
        var chain = new VoiceChain(preset);
        Assert.Equal(withPsola, chain.UsesPitch);
        double baseHz = withPsola ? 150 * DspMath.SemitonesToRatio(0.5) : 220;
        var input = withPsola ? TestSignals.Vowel(150, 3.0) : TestSignals.Sine(220, 3.0);
        var result = TestSignals.ProcessInBlocks(chain, input);

        double low = double.MaxValue, high = double.MinValue;
        for (double t = 0.5; t + 0.02 <= 2.8; t += 0.005)
        {
            double semitones = 12 * Math.Log2(TestSignals.MeasureF0(TestSignals.Segment(result, t, t + 0.02), 100, 500) / baseHz);
            low = Math.Min(low, semitones);
            high = Math.Max(high, semitones);
        }
        output.WriteLine($"vibrato {(withPsola ? "PSOLA" : "retardo")}: de {low:+0.00;-0.00} a {high:+0.00;-0.00} semitonos, latencia {chain.LatencySamples * 1000.0 / Rate:F1} ms");
        Assert.InRange(high - low, 1.2, 2.3);
        Assert.InRange((high + low) / 2, -0.3, 0.3);
    }

    [Fact]
    public void Switching_voice_crossfades_without_clicks()
    {
        var processor = new VoiceProcessor();
        var input = TestSignals.Sine(220, 1.0, amplitude: 0.5f);
        var result = (float[])input.Clone();
        int block = 480;
        for (int offset = 0, n = 0; offset < result.Length; offset += block, n++)
        {
            if (n == 50) processor.SetPreset(BuiltInVoices.Find("radio")!);
            processor.Process(result.AsSpan(offset, block));
        }

        // Un seno de 220 Hz y 0,5 de amplitud cambia como mucho 0,0144 por muestra; un clic daría saltos mucho mayores.
        float maxJump = 0;
        for (int i = 50 * block - 200; i < 50 * block + 3000; i++) maxJump = Math.Max(maxJump, Math.Abs(result[i] - result[i - 1]));
        output.WriteLine($"salto máximo alrededor del cambio: {maxJump:F4}");
        Assert.True(maxJump < 0.1f);
    }

    [Fact]
    public void Live_edit_updates_parameters_in_place()
    {
        var processor = new VoiceProcessor();
        var mujer = BuiltInVoices.Find("mujer")!;
        processor.SetPreset(mujer);
        processor.Process(new float[480]);

        var edited = mujer with { Id = "custom", PitchSemitones = 7 };
        processor.SetPreset(edited, liveEdit: true);
        Assert.Equal(7, processor.CurrentPreset.PitchSemitones);

        var input = TestSignals.Vowel(150, 1.5);
        var result = TestSignals.ProcessInBlocks(processor, input);
        double f0 = TestSignals.MeasureF0(TestSignals.Segment(result, 0.6, 1.4));
        Assert.InRange(f0, 150 * DspMath.SemitonesToRatio(7) * 0.98, 150 * DspMath.SemitonesToRatio(7) * 1.02);
    }

    [Fact]
    public void Presets_roundtrip_through_json()
    {
        foreach (var preset in BuiltInVoices.All)
        {
            string json = JsonSerializer.Serialize(preset);
            Assert.Equal(preset, JsonSerializer.Deserialize<VoicePreset>(json));
        }
    }
}
