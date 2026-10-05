using System.Diagnostics;
using M0DV0IC3.Audio;
using M0DV0IC3.Audio.NoiseSuppression;
using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Presets;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class PipelineTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    [Fact]
    public void RnNoise_loads_and_removes_fan_like_noise()
    {
        using var rn = RnNoiseEffect.TryCreate(out string? error);
        Assert.True(rn is not null, error);

        // Ruido grave tipo ventilador. RNNoise está entrenado con ruido real: el ruido blanco fuerte lo
        // confunde con fricativas y apenas lo atenúa, así que no sirve como prueba.
        var noise = TestSignals.Noise(3.0, DspMath.DbToGain(-20));
        for (int pass = 0; pass < 2; pass++)
        {
            var lowPass = new M0DV0IC3.Dsp.Filters.Biquad();
            lowPass.SetLowPass(Rate, 400);
            lowPass.Process(noise);
        }
        var result = TestSignals.ProcessInBlocks(rn!, noise);
        double before = DspMath.GainToDb(DspMath.Rms(TestSignals.Segment(noise, 2, 3)));
        double after = DspMath.GainToDb(DspMath.Rms(TestSignals.Segment(result, 2, 3)));
        output.WriteLine($"ruido {before:F1} dBFS → {after:F1} dBFS");
        Assert.True(after < before - 20);
        Assert.Equal(RnNoiseEffect.FrameSize, rn!.LatencySamples);
    }

    [Fact]
    public void RnNoise_with_small_packets_adds_at_most_one_frame()
    {
        using var rn = RnNoiseEffect.TryCreate(out string? error);
        Assert.True(rn is not null, error);
        TestSignals.ProcessInBlocks(rn!, TestSignals.Noise(1.0, 0.1f), block: 144);
        Assert.InRange(rn!.LatencySamples, RnNoiseEffect.FrameSize, 2 * RnNoiseEffect.FrameSize);
    }

    [Fact]
    public void App_audio_is_mixed_into_the_cable_but_not_into_the_monitor()
    {
        using var pipeline = new VoicePipeline(loadNoiseSuppression: false);
        pipeline.MonitorVoice = true;
        var music = new ConstantSource(0.4f);
        pipeline.AppAudio = music;
        pipeline.AppAudioGain = 0.5f;

        var silence = new float[480];
        var cable = new float[480];
        var monitor = new float[480];
        pipeline.Process(silence, cable, monitor);
        Assert.All(cable, v => Assert.Equal(0.2f, v, 1e-4f));
        Assert.All(monitor, v => Assert.Equal(0f, v));
        Assert.Equal(0.2f, pipeline.ReadAppAudioPeak(), 1e-4f);

        pipeline.Muted = true;
        pipeline.Process(silence, cable, monitor);
        Assert.All(cable, v => Assert.Equal(0f, v));

        pipeline.Muted = false;
        pipeline.AppAudio = null;
        pipeline.Process(silence, cable, monitor);
        Assert.All(cable, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Soundboard_limits_voices_and_stops_with_fade()
    {
        var mixer = new SoundboardMixer();
        var clips = Enumerable.Range(0, 10).Select(i => new SoundClip($"s{i}", $"s{i}", TestSignals.Sine(300 + i * 50, 2.0, 0.1f))).ToArray();
        foreach (var clip in clips) mixer.Play(clip);

        var block = new float[480];
        Assert.True(mixer.Render(block));
        Assert.Equal(SoundboardMixer.MaxVoices, mixer.ActiveVoices);

        mixer.StopAll();
        mixer.Render(block);
        Assert.False(mixer.Render(block));
        Assert.Equal(0, mixer.ActiveVoices);
        Assert.All(block, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Soundboard_retrigger_restarts_the_same_clip()
    {
        var mixer = new SoundboardMixer();
        var clip = new SoundClip("a", "a", TestSignals.Sine(500, 1.0, 0.2f));
        mixer.Play(clip);
        mixer.Render(new float[4800]);
        mixer.Play(clip);
        mixer.Render(new float[480]);
        Assert.Equal(1, mixer.ActiveVoices);
    }

    [Fact]
    public void Pipeline_mixes_sounds_and_respects_monitor_switches()
    {
        using var pipeline = new VoicePipeline(loadNoiseSuppression: false);
        pipeline.Soundboard.Play(new SoundClip("beep", "beep", TestSignals.Sine(1000, 1.0, 0.3f)));
        var mic = new float[480];
        var cable = new float[480];
        var monitor = new float[480];

        pipeline.MonitorVoice = false;
        pipeline.MonitorSounds = false;
        pipeline.Process(mic, cable, monitor);
        Assert.True(DspMath.Peak(cable) > 0.2f);
        Assert.Equal(0f, DspMath.Peak(monitor));

        pipeline.MonitorSounds = true;
        pipeline.Process(mic, cable, monitor);
        Assert.True(DspMath.Peak(monitor) > 0.2f);
    }

    [Fact]
    public void Muted_pipeline_outputs_pure_silence_from_the_first_sample()
    {
        using var pipeline = new VoicePipeline(loadNoiseSuppression: false);
        pipeline.Muted = true;
        pipeline.MonitorVoice = true;
        pipeline.Soundboard.Play(new SoundClip("beep", "beep", TestSignals.Sine(1000, 1.0, 0.3f)));
        var mic = TestSignals.Vowel(150, 0.5, amplitude: 0.8f);
        var cable = new float[480];
        var monitor = new float[480];

        for (int offset = 0; offset + 480 <= mic.Length; offset += 480)
        {
            pipeline.Process(mic.AsSpan(offset, 480), cable, monitor);
            Assert.All(cable, v => Assert.Equal(0f, v));
            Assert.All(monitor, v => Assert.Equal(0f, v));
        }
        Assert.True(pipeline.ReadInputPeak() > 0.5f, "la entrada se sigue midiendo");
    }

    [Theory]
    [InlineData("mujer")]
    [InlineData("mujer2")]
    [InlineData("autotune")]
    [InlineData("robot")]
    [InlineData("fantasma")]
    [InlineData("invertida")]
    [InlineData("agua")]
    [InlineData("espacial")]
    [InlineData("cueva")]
    public void Pipeline_does_not_allocate_on_the_audio_path(string voiceId)
    {
        using var pipeline = new VoicePipeline();
        Assert.True(pipeline.NoiseSuppressionAvailable, pipeline.NoiseSuppressionError);
        pipeline.NoiseSuppression = true;
        pipeline.Gate.ThresholdDb = -60;
        pipeline.MonitorVoice = true;
        pipeline.Voice.SetPreset(BuiltInVoices.Find(voiceId)!);
        pipeline.Soundboard.Play(new SoundClip("x", "x", TestSignals.Sine(700, 20.0, 0.1f)));

        // Música de una app, por su ring buffer con compensación de deriva, como la escribe AppAudioCapture.
        var musicRing = new SpscRingBuffer(1 << 15);
        pipeline.AppAudio = new DriftCompensatedReader(musicRing, 576);
        var music = TestSignals.Sine(440, 12.0, 0.2f);
        int musicWritten = 0;

        var input = TestSignals.Vowel(140, 10.0, vibratoHz: 5);
        var cable = new float[480];
        var monitor = new float[480];
        for (int i = 0; i < 100; i++) Step(input.AsSpan(i * 480, 480));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int offset = 0; offset + 480 <= input.Length; offset += 480) Step(input.AsSpan(offset, 480));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);

        void Step(ReadOnlySpan<float> mic)
        {
            musicWritten += musicRing.Write(music.AsSpan(musicWritten % (music.Length - 480), 480));
            pipeline.Process(mic, cable, monitor);
        }
    }

    private sealed class ConstantSource(float value) : IRenderSource
    {
        public void Render(Span<float> destination) => destination.Fill(value);
    }
}

/// <summary>Se ejecuta sin otros tests en paralelo: si compite por la CPU, las medidas de tiempo no valen.</summary>
[CollectionDefinition(nameof(PerformanceCollection), DisableParallelization = true)]
public sealed class PerformanceCollection;

[Collection(nameof(PerformanceCollection))]
public sealed class PerformanceTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

#if DEBUG
    private const double LocalLimit = 0.30;
#else
    // Las más caras: el coro (tres PSOLA más) y una voz con tono objetivo + RNNoise, que rondan el 6-7 % y en un PC
    // ocupado llegan al 9 %. Con un límite del 8 % la prueba fallaba al azar.
    private const double LocalLimit = 0.12;
#endif

    // En los servidores de integración continua (GitHub Actions pone CI=true) la CPU es compartida y las medidas
    // varían mucho: ahí solo se buscan regresiones graves.
    private static double Limit => Environment.GetEnvironmentVariable("CI") == "true" ? 0.30 : LocalLimit;

    [Fact]
    public void Every_voice_runs_well_under_real_time()
    {
        var input = TestSignals.Vowel(140, 10.0, vibratoHz: 5);
        var cable = new float[480];
        var report = new List<string>();

        foreach (var preset in BuiltInVoices.All.Append(VoicePreset.Neutral))
        {
            using var pipeline = new VoicePipeline();
            pipeline.NoiseSuppression = preset.Id == "mujer";
            pipeline.Voice.SetPreset(preset);
            pipeline.Process(input.AsSpan(0, Rate), new float[Rate], Span<float>.Empty);

            var watch = Stopwatch.StartNew();
            for (int offset = 0; offset + 480 <= input.Length; offset += 480)
                pipeline.Process(input.AsSpan(offset, 480), cable, Span<float>.Empty);
            watch.Stop();

            double factor = watch.Elapsed.TotalSeconds / 10.0;
            report.Add($"{preset.Id,-10} {factor * 100,6:F2} % tiempo real{(pipeline.NoiseSuppression ? " (con RNNoise)" : "")}");
            Assert.True(factor < Limit, $"{preset.Id} usa {factor:P1} del tiempo real");
        }
        foreach (string line in report) output.WriteLine(line);
    }
}
