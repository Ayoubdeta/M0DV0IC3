using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class AutotuneTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    // La menor: notas blancas del piano.
    private static readonly int[] AMinorPitchClasses = [9, 11, 0, 2, 4, 5, 7];

    private static readonly VoicePreset HardAMinor = VoicePreset.Neutral with
    {
        Id = "test", AutotuneScale = AutotuneScale.Minor, AutotuneKey = 9, AutotuneRetuneMs = 0,
    };

    [Fact]
    public void Corrector_snaps_to_the_nearest_note_of_the_scale()
    {
        var corrector = new PitchCorrector(Rate);
        corrector.Configure(AutotuneScale.Chromatic, 0, 0);
        Assert.Equal(440.0 / 445.0, corrector.NextRatio(445, 0, 0), 9);
        Assert.Equal(69, corrector.CurrentNote);

        // Entre Do♯3 y Re3: en La menor no hay Do♯, así que va a Re3.
        corrector.Release();
        corrector.Configure(AutotuneScale.Minor, 9, 0);
        double ratio = corrector.NextRatio(Hz(49.4), 0, 0);
        Assert.Equal(50, corrector.CurrentNote);
        Assert.Equal(Hz(50), Hz(49.4) * ratio, 6);
    }

    [Fact]
    public void Corrector_hysteresis_keeps_the_note_near_the_boundary()
    {
        var corrector = new PitchCorrector(Rate);
        corrector.Configure(AutotuneScale.Chromatic, 0, 0);
        corrector.NextRatio(Hz(60), 0, 240);
        corrector.NextRatio(Hz(60.53), 0, 240);
        Assert.Equal(60, corrector.CurrentNote);
        corrector.NextRatio(Hz(60.58), 0, 240);
        Assert.Equal(61, corrector.CurrentNote);
        corrector.NextRatio(Hz(60.47), 0, 240);
        Assert.Equal(61, corrector.CurrentNote);
        corrector.NextRatio(Hz(60.42), 0, 240);
        Assert.Equal(60, corrector.CurrentNote);
    }

    [Fact]
    public void Corrector_transposes_before_choosing_the_note()
    {
        var corrector = new PitchCorrector(Rate);
        corrector.Configure(AutotuneScale.Chromatic, 0, 0);
        double ratio = corrector.NextRatio(Hz(57.2), 12, 0);
        Assert.Equal(69, corrector.CurrentNote);
        Assert.Equal(Hz(69), Hz(57.2) * ratio, 6);
    }

    [Fact]
    public void Exaggeration_widens_the_melody_around_the_recent_average()
    {
        var corrector = new PitchCorrector(Rate);
        corrector.Configure(AutotuneScale.Chromatic, 0, 0, exaggeration: 1);
        for (int i = 0; i < 20; i++) corrector.NextRatio(Hz(60), 0, 400);
        Assert.Equal(60, corrector.CurrentNote);

        // 0,8 semitonos por encima del tono medio se convierten en 2 (×2,5): de 60,8 a la nota 62.
        double ratio = corrector.NextRatio(Hz(60.8), 0, 400);
        Assert.Equal(62, corrector.CurrentNote);
        Assert.Equal(Hz(62), Hz(60.8) * ratio, 6);
    }

    [Fact]
    public void Exaggerated_autotune_makes_speech_intonation_jump_more_notes()
    {
        // Entonación de habla: el tono sube y baja ±1 semitono alrededor de 125 Hz cada 0,4 s.
        var input = TestSignals.Vowel(t => 125 * Math.Pow(2, Math.Sin(2 * Math.PI * 1.25 * t) / 12), 4.0);
        double plain = PitchSpan(VoicePreset.Neutral with { Id = "a", AutotuneScale = AutotuneScale.Chromatic }, input);
        double exaggerated = PitchSpan(VoicePreset.Neutral with { Id = "b", AutotuneScale = AutotuneScale.Chromatic, AutotuneExaggeration = 0.8 }, input);
        output.WriteLine($"recorrido de la melodía: sin exagerar {plain:F1} semitonos, exagerado {exaggerated:F1} semitonos");

        Assert.InRange(plain, 1.5, 2.5);
        Assert.True(exaggerated >= 1.8 * plain, $"{exaggerated:F1} frente a {plain:F1}");

        double PitchSpan(VoicePreset preset, float[] signal)
        {
            var result = TestSignals.ProcessInBlocks(new VoiceChain(preset), signal);
            var semitones = new List<double>();
            for (double t = 1.5; t + 0.03 <= 3.9; t += 0.01)
                semitones.Add(12 * Math.Log2(TestSignals.MeasureF0(TestSignals.Segment(result, t, t + 0.03), 70, 400) / 125));
            semitones.Sort();
            return semitones[(int)(semitones.Count * 0.95)] - semitones[(int)(semitones.Count * 0.05)];
        }
    }

    [Fact]
    public void Slow_retune_glides_into_the_note()
    {
        var corrector = new PitchCorrector(Rate);
        corrector.Configure(AutotuneScale.Chromatic, 0, retuneMs: 100);
        Assert.Equal(1.0, corrector.NextRatio(Hz(60.4), 0, 0), 9);

        // Tras una constante de tiempo (100 ms) se ha corregido el 63 % de los 0,4 semitonos.
        double ratio = 1;
        for (int i = 0; i < 10; i++) ratio = corrector.NextRatio(Hz(60.4), 0, 480);
        Assert.InRange(Midi(Hz(60.4) * ratio), 60.4 - 0.4 * 0.7, 60.4 - 0.4 * 0.55);
    }

    [Fact]
    public void Hard_autotune_holds_an_off_key_note_perfectly_flat()
    {
        // 143 Hz con un vibrato de ±3 % (±51 cents), entre Do♯3 y Re3: debe salir Re3 clavado y sin vibrato.
        var input = TestSignals.Vowel(143, 2.0, vibratoHz: 5);
        var result = TestSignals.ProcessInBlocks(new VoiceChain(HardAMinor), input);

        double worst = 0;
        for (double t = 0.5; t + 0.04 <= 1.9; t += 0.02)
        {
            double f0 = TestSignals.MeasureF0(TestSignals.Segment(result, t, t + 0.04), 80, 400);
            worst = Math.Max(worst, Math.Abs(1200 * Math.Log2(f0 / Hz(50))));
        }
        output.WriteLine($"desviación máxima respecto de Re3 ({Hz(50):F2} Hz): {worst:F1} cents");
        Assert.True(worst < 10, $"{worst:F1} cents");
    }

    [Fact]
    public void Hard_autotune_turns_a_glide_into_steps_of_the_scale()
    {
        var input = TestSignals.Glide(110, 175, 3.0);
        var result = TestSignals.ProcessInBlocks(new VoiceChain(HardAMinor), input);

        int total = 0, tuned = 0, tunedInput = 0;
        for (double t = 0.3; t + 0.04 <= 2.9; t += 0.02, total++)
        {
            if (CentsFromAMinor(TestSignals.MeasureF0(TestSignals.Segment(result, t, t + 0.04), 80, 400)) <= 20) tuned++;
            if (CentsFromAMinor(TestSignals.MeasureF0(TestSignals.Segment(input, t, t + 0.04), 80, 400)) <= 20) tunedInput++;
        }
        output.WriteLine($"ventanas afinadas (±20 cents de La menor): salida {tuned}/{total}, entrada {tunedInput}/{total}");
        Assert.True(tuned >= 0.8 * total, $"{tuned}/{total}");
        Assert.True(tunedInput <= 0.45 * total, "la entrada de prueba no debería estar afinada");
    }

    [Fact]
    public void Built_in_autotune_voices_are_hard_tuned()
    {
        var talk = BuiltInVoices.Find("autotune")!;
        Assert.Equal(AutotuneScale.Minor, talk.AutotuneScale);
        Assert.Equal(9, talk.AutotuneKey);
        Assert.Equal(0, talk.AutotuneRetuneMs);
        Assert.True(talk.AutotuneExaggeration > 0.5, "al hablar, la melodía se exagera");
        Assert.Equal(1.0, talk.FormantRatio);

        var sing = BuiltInVoices.Find("autotune-cantar")!;
        Assert.Equal(AutotuneScale.Chromatic, sing.AutotuneScale);
        Assert.Equal(0, sing.AutotuneRetuneMs);
        Assert.Equal(0, sing.AutotuneExaggeration);
        Assert.True(talk.UsesPitch && sing.UsesPitch);
    }

    private static double Midi(double hz) => 69 + 12 * Math.Log2(hz / 440);

    private static double Hz(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    private static double CentsFromAMinor(double hz)
    {
        if (!double.IsFinite(hz)) return double.MaxValue;
        double midi = Midi(hz);
        double best = double.MaxValue;
        for (int n = (int)midi - 3; n <= (int)midi + 3; n++)
            if (AMinorPitchClasses.Contains(((n % 12) + 12) % 12)) best = Math.Min(best, Math.Abs(midi - n) * 100);
        return best;
    }
}
