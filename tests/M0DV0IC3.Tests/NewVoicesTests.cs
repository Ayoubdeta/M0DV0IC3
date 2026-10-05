using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

/// <summary>Tono objetivo, coro, vocoder, transmisión y compresor.</summary>
public sealed class NewVoicesTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    // La queja que lo motivó: con +5 semitonos fijos, una voz grave (~100 Hz) seguía sonando a hombre.
    [Theory]
    [InlineData("mujer", 95)]
    [InlineData("mujer", 140)]
    [InlineData("mujer2", 95)]
    [InlineData("nino", 110)]
    [InlineData("anime", 100)]
    [InlineData("grave", 140)]
    [InlineData("gigante", 120)]
    public void Target_pitch_voices_reach_their_pitch_from_any_voice(string voiceId, double inputHz)
    {
        var preset = BuiltInVoices.Find(voiceId)!;
        // Sin vibrato: con él, la autocorrelación de MeasureF0 se equivoca en las voces muy graves con formantes
        // desplazados (lo comprobé con otro detector: el tono era el correcto).
        var input = TestSignals.Vowel(inputHz, 3.0);
        var chain = new VoiceChain(preset);
        var result = TestSignals.ProcessInBlocks(chain, input);

        // El cambio calculado, y el tono que sale de verdad (buscado cerca del objetivo: los formantes desplazados
        // y los filtros engañan a la autocorrelación si se busca en todo el rango).
        // El cambio se limita a una octava hacia abajo y dos hacia arriba.
        double expected = Math.Clamp(12 * Math.Log2(preset.TargetPitchHz / inputHz), -12, 24);
        double expectedHz = inputHz * DspMath.SemitonesToRatio(expected);
        double shift = chain.PitchShifter!.EffectiveSemitones;
        double f0 = TestSignals.MeasureF0(TestSignals.Segment(result, 2.0, 2.9), expectedHz / 1.4, expectedHz * 1.4);
        output.WriteLine($"{voiceId}: {inputHz} Hz → {shift:+0.0;-0.0} st → {f0:F1} Hz (objetivo {preset.TargetPitchHz} Hz)");
        Assert.InRange(shift, expected - 0.5, expected + 0.5);
        Assert.InRange(f0, expectedHz * 0.95, expectedHz * 1.05);
    }

    [Fact]
    public void Target_pitch_never_goes_the_wrong_way()
    {
        // Para "Mujer" (sube), una voz que ya es más aguda que el objetivo sube igualmente 2 semitonos.
        var input = TestSignals.Vowel(260, 3.0);
        var result = TestSignals.ProcessInBlocks(new VoiceChain(BuiltInVoices.Find("mujer")!), input);
        double f0 = TestSignals.MeasureF0(TestSignals.Segment(result, 2.0, 2.9), 200, 450);
        output.WriteLine($"260 Hz con Mujer → {f0:F1} Hz");
        Assert.InRange(f0, 260 * DspMath.SemitonesToRatio(2) * 0.97, 260 * DspMath.SemitonesToRatio(2) * 1.03);
    }

    [Fact]
    public void Pitch_profile_learns_the_average_and_ignores_octave_errors()
    {
        var profile = new PitchProfile();
        Assert.False(profile.IsLearned);
        for (int i = 0; i < 300; i++) profile.Add(100, 0.01);
        Assert.True(profile.IsLearned);
        for (int i = 0; i < 50; i++) profile.Add(400, 0.01); // dos octavas: error del detector
        Assert.InRange(profile.CenterHz, 99, 101);
    }

    [Fact]
    public void Harmonizer_adds_an_octave_below()
    {
        var input = TestSignals.Vowel(200, 1.5);
        var harmonizer = new Harmonizer(Rate, VoiceRange.Medium);
        harmonizer.Configure(1);
        var result = TestSignals.ProcessInBlocks(harmonizer, input);

        var segment = TestSignals.Segment(result, 0.8, 1.4);
        double sub = Magnitude(segment, 100);
        double original = Magnitude(TestSignals.Segment(input, 0.8, 1.4), 100);
        output.WriteLine($"100 Hz: entrada {DspMath.GainToDb(original):F1} dB, coro {DspMath.GainToDb(sub):F1} dB");
        Assert.True(sub > original * 10, "el coro debería tener una voz una octava por debajo");
    }

    [Fact]
    public void Vocoder_turns_the_voice_into_the_chord()
    {
        var input = TestSignals.Vowel(150, 1.0);
        var vocoder = new Vocoder(Rate);
        vocoder.Configure(1, 110);
        var result = TestSignals.ProcessInBlocks(vocoder, input);

        var segment = TestSignals.Segment(result, 0.3, 0.9);
        double chord = Magnitude(segment, 220);   // octava de la tónica del acorde
        double voice = Magnitude(segment, 150);   // el tono de la voz ya no debería estar
        output.WriteLine($"220 Hz (acorde) {DspMath.GainToDb(chord):F1} dB, 150 Hz (voz) {DspMath.GainToDb(voice):F1} dB, RMS {DspMath.GainToDb(DspMath.Rms(segment)):F1} dB");
        Assert.True(chord > voice * 3);
        Assert.InRange(DspMath.GainToDb(DspMath.Rms(segment)), -40, -3);
    }

    [Fact]
    public void Walkie_beeps_when_you_stop_talking_and_stays_silent_after()
    {
        var input = new float[Rate * 5 / 2];
        TestSignals.Vowel(140, 0.5).CopyTo(input, Rate / 4);
        var walkie = new Transmission(Rate);
        walkie.Configure(TransmissionStyle.Walkie, 1);
        var result = TestSignals.ProcessInBlocks(walkie, input);

        // La vocal acaba en 0,75 s. Cuando la envolvente baja de -46 dB y pasan 350 ms más, suena el pitido de
        // 1250 Hz (120 ms) y luego 160 ms de ruido: todo dentro de 0,9-1,8 s.
        double beep = Magnitude(TestSignals.Segment(result, 0.9, 1.8), 1250);
        double during = Magnitude(TestSignals.Segment(result, 0.3, 0.7), 1250);
        output.WriteLine($"1250 Hz: mientras hablas {DspMath.GainToDb(during):F1} dB, al acabar {DspMath.GainToDb(beep):F1} dB");
        Assert.True(beep > 0.005 && beep > during * 4);
        Assert.Equal(0f, DspMath.Peak(TestSignals.Segment(result, 0.0, 0.2)));
        Assert.Equal(0f, DspMath.Peak(TestSignals.Segment(result, 2.0, 2.5)));
    }

    [Fact]
    public void Compressor_brings_quiet_and_loud_closer()
    {
        var quiet = TestSignals.Vowel(140, 1.0, amplitude: 0.05f);
        var loud = TestSignals.Vowel(140, 1.0, amplitude: 0.5f);
        var compressor = new Compressor(Rate);
        compressor.Configure(0.8);
        double inputRange = DspMath.GainToDb(DspMath.Rms(loud) / DspMath.Rms(quiet));
        var q = TestSignals.ProcessInBlocks(compressor, quiet);
        compressor.Reset();
        var l = TestSignals.ProcessInBlocks(compressor, loud);
        double outputRange = DspMath.GainToDb(DspMath.Rms(l.AsSpan(Rate / 2)) / DspMath.Rms(q.AsSpan(Rate / 2)));
        output.WriteLine($"diferencia flojo/fuerte: {inputRange:F1} dB → {outputRange:F1} dB");
        Assert.True(outputRange < inputRange - 8);
    }

    /// <summary>Amplitud de una frecuencia (Goertzel con ventana de Hann).</summary>
    private static double Magnitude(ReadOnlySpan<float> x, double hz)
    {
        double w = 2 * Math.PI * hz / Rate, coeff = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (x.Length - 1));
            double s0 = x[i] * hann + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return 4 * Math.Sqrt(Math.Max(power, 0)) / x.Length;
    }
}
