using M0DV0IC3.Audio;
using M0DV0IC3.Audio.Recording;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Audio.Speech;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Presets;
using NAudio.Wave;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class SpeechAndRecordingTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    private static (float[] Cable, float[] Monitor) Run(VoicePipeline pipeline, double seconds, Action<int>? atBlock = null)
    {
        int total = (int)(seconds * Rate);
        var input = new float[480];
        var cable = new float[total];
        var monitor = new float[total];
        for (int offset = 0, block = 0; offset + 480 <= total; offset += 480, block++)
        {
            atBlock?.Invoke(block);
            pipeline.Process(input, cable.AsSpan(offset, 480), monitor.AsSpan(offset, 480));
        }
        return (cable, monitor);
    }

    [Fact]
    public void A_phrase_goes_out_through_the_mic_with_the_active_voice()
    {
        using var pipeline = new VoicePipeline(loadNoiseSuppression: false);
        pipeline.Speech.Voice.SetPreset(VoicePreset.Neutral with { Id = "test", PitchSemitones = 12 });
        var phrase = new SoundClip("frase", "frase", TestSignals.Vowel(150, 1.0));
        pipeline.Speech.Play(phrase, SpeechPlayer.EstimatePitch(phrase.Samples));

        var (cable, monitor) = Run(pipeline, 1.5);
        double f0 = TestSignals.MeasureF0(TestSignals.Segment(cable, 0.3, 0.9));
        output.WriteLine($"frase a 150 Hz con +12 semitonos: {f0:F1} Hz en el micro");
        Assert.InRange(f0, 291, 309);
        // Con «Sonidos en mis auriculares» (por defecto) también la oyes tú.
        Assert.True(DspMath.Rms(TestSignals.Segment(monitor, 0.3, 0.9)) > 0.01);
        // El tono de la frase no se mezcla con lo aprendido de tu voz.
        Assert.False(pipeline.Voice.Profile.IsLearned);
    }

    [Fact]
    public void Stopping_a_phrase_silences_it_at_once()
    {
        using var pipeline = new VoicePipeline(loadNoiseSuppression: false);
        pipeline.Speech.Voice.SetPreset(VoicePreset.Neutral with { Id = "eco", EchoMs = 250, EchoFeedback = 0.6, EchoMix = 0.5 });
        pipeline.Speech.Play(new SoundClip("frase", "frase", TestSignals.Vowel(150, 2.0)));

        int stopBlock = Rate / 2 / 480;
        var (cable, _) = Run(pipeline, 1.0, block =>
        {
            if (block == stopBlock) pipeline.Speech.Stop();
        });
        double stopAt = stopBlock * 480.0 / Rate;
        double before = DspMath.GainToDb(DspMath.Rms(TestSignals.Segment(cable, stopAt - 0.1, stopAt)));
        double after = DspMath.GainToDb(DspMath.Rms(TestSignals.Segment(cable, stopAt + 0.02, 1.0)));
        output.WriteLine($"antes de parar: {before:F1} dB, después: {after:F1} dB (con eco)");
        Assert.True(after < -90);
        Assert.False(pipeline.Speech.IsPlaying);
    }

    [Fact]
    public void Phrase_pitch_is_estimated()
    {
        double f0 = SpeechPlayer.EstimatePitch(TestSignals.Vowel(120, 1.0));
        output.WriteLine($"vocal a 120 Hz: {f0:F1} Hz");
        Assert.InRange(f0, 116, 124);
        Assert.Equal(0, SpeechPlayer.EstimatePitch(new float[Rate]));
    }

    [Fact]
    public void Speech_does_not_allocate_while_playing()
    {
        var player = new SpeechPlayer(480);
        player.Voice.SetPreset(VoicePreset.Neutral with { Id = "tono", PitchSemitones = 5, ReverbMix = 0.3 });
        player.Play(new SoundClip("frase", "frase", TestSignals.Vowel(150, 1.0)));
        var block = new float[480];
        player.Render(block);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) player.Render(block);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void The_recording_has_what_went_out_through_the_mic_and_becomes_an_mp3()
    {
        string folder = Path.Combine(Path.GetTempPath(), "m0dv0ic3-tests", Guid.NewGuid().ToString("N"));
        string wav = Path.Combine(folder, "grabacion.wav");
        try
        {
            using var pipeline = new VoicePipeline(loadNoiseSuppression: false);
            var beep = new SoundClip("pitido", "pitido", TestSignals.Sine(440, 1.0, 0.3f));

            // Lo que suena antes de grabar no entra en la grabación.
            pipeline.Soundboard.Play(beep);
            Run(pipeline, 0.5);

            pipeline.Recorder.Start(wav);
            pipeline.Soundboard.Play(beep);
            Run(pipeline, 1.5);
            var length = pipeline.Recorder.Stop();
            Assert.Equal(0, pipeline.Recorder.SamplesDropped);

            float[] recorded;
            using (var reader = new WaveFileReader(wav))
            {
                Assert.Equal(Rate, reader.WaveFormat.SampleRate);
                Assert.Equal(1, reader.WaveFormat.Channels);
                var provider = reader.ToSampleProvider();
                recorded = new float[(int)(reader.Length / 2)];
                provider.Read(recorded);
            }
            output.WriteLine($"grabado: {length.TotalSeconds:F2} s");
            Assert.InRange(length.TotalSeconds, 1.49, 1.51);
            // El pitido de 1 s y luego silencio.
            Assert.InRange(DspMath.Rms(TestSignals.Segment(recorded, 0.1, 0.9)), 0.19, 0.23);
            Assert.True(DspMath.Rms(TestSignals.Segment(recorded, 1.1, 1.5)) < 1e-4);

            string mp3 = MicRecorder.ConvertToMp3(wav);
            output.WriteLine($"queda: {Path.GetFileName(mp3)}");
            Assert.Equal(".mp3", Path.GetExtension(mp3));
            Assert.False(File.Exists(wav));
            using var decoded = new MediaFoundationReader(mp3);
            Assert.InRange(decoded.TotalTime.TotalSeconds, 1.4, 1.65);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
