using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Pitch;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class PitchTests(ITestOutputHelper output)
{
    private const int Rate = TestSignals.Rate;

    [Theory]
    [InlineData(100)]
    [InlineData(155)]
    [InlineData(220)]
    [InlineData(400)]
    public void Yin_detects_sine_frequency(double hz)
    {
        var yin = new YinPitchDetector(Rate, 65, 900);
        foreach (float x in TestSignals.Sine(hz, 0.5)) yin.Push(x);

        Assert.True(yin.IsVoiced);
        double detected = Rate / yin.PeriodSamples;
        Assert.InRange(detected, hz * 0.99, hz * 1.01);
    }

    [Theory]
    [InlineData(95)]
    [InlineData(130)]
    [InlineData(210)]
    public void Yin_detects_vowel_fundamental(double hz)
    {
        var yin = new YinPitchDetector(Rate, 65, 900);
        foreach (float x in TestSignals.Vowel(hz, 0.5)) yin.Push(x);

        Assert.True(yin.IsVoiced);
        Assert.InRange(Rate / yin.PeriodSamples, hz * 0.98, hz * 1.02);
    }

    [Fact]
    public void Yin_reports_silence_and_noise_as_unvoiced()
    {
        var yin = new YinPitchDetector(Rate, 65, 900);
        foreach (float x in new float[Rate / 2]) yin.Push(x);
        Assert.False(yin.IsVoiced);

        int voiced = 0, analyses = 0;
        var noise = TestSignals.Noise(1.0, 0.3f);
        for (int i = 0; i < noise.Length; i++)
        {
            yin.Push(noise[i]);
            if (i % 256 == 255)
            {
                analyses++;
                if (yin.IsVoiced) voiced++;
            }
        }
        Assert.True(voiced < analyses * 0.2, $"ruido detectado como sonoro en {voiced}/{analyses} análisis");
    }

    // Cada milisegundo que tarda en detectar una vocal sale sin cambiar de tono y luego salta: en cada sílaba.
    // Con la ventana antigua (la más vieja de la trama) tardaba 38-43 ms; ahora, 17-22 ms.
    [Theory]
    [InlineData(110)]
    [InlineData(130)]
    [InlineData(210)]
    public void Yin_detects_vowel_onset_within_25_ms(double hz)
    {
        var yin = new YinPitchDetector(Rate, 65, 900);
        foreach (float x in new float[Rate / 4]) yin.Push(x);
        Assert.False(yin.IsVoiced);

        var vowel = TestSignals.Vowel(hz, 0.2);
        int detectedAt = -1;
        for (int i = 0; i < vowel.Length && detectedAt < 0; i++)
        {
            yin.Push(vowel[i]);
            if (yin.IsVoiced) detectedAt = i;
        }

        double ms = detectedAt * 1000.0 / Rate;
        output.WriteLine($"{hz} Hz: vocal detectada a los {ms:F1} ms, f0 {Rate / yin.PeriodSamples:F1} Hz");
        Assert.InRange(ms, 0, 25);
        Assert.InRange(Rate / yin.PeriodSamples, hz * 0.97, hz * 1.03);
    }

    [Theory]
    [InlineData(155, 12)]
    [InlineData(155, -5)]
    [InlineData(200, 5)]
    [InlineData(120, 8)]
    [InlineData(130, -8)]
    public void Psola_shifts_pitch_by_semitones(double inputHz, double semitones)
    {
        var psola = new PsolaPitchShifter(Rate, VoiceRange.Low);
        psola.SetParameters(DspMath.SemitonesToRatio(semitones), 1.0, 0);
        var result = TestSignals.ProcessInBlocks(psola, TestSignals.Vowel(inputHz, 1.5));

        double expected = inputHz * DspMath.SemitonesToRatio(semitones);
        double measured = TestSignals.MeasureF0(TestSignals.Segment(result, 0.5, 1.4), 40, 1200);
        output.WriteLine($"{inputHz} Hz {semitones:+0;-0} st → esperado {expected:F1} Hz, medido {measured:F1} Hz, latencia {psola.LatencySamples * 1000.0 / Rate:F1} ms");
        Assert.InRange(measured, expected * 0.98, expected * 1.02);
        Assert.All(result, x => Assert.True(float.IsFinite(x)));
    }

    [Fact]
    public void Psola_formant_shift_keeps_pitch_and_raises_brightness()
    {
        var input = TestSignals.Vowel(140, 1.5);
        var psola = new PsolaPitchShifter(Rate, VoiceRange.Medium);
        psola.SetParameters(1.0, 1.25, 0);
        var result = TestSignals.ProcessInBlocks(psola, input);

        double f0 = TestSignals.MeasureF0(TestSignals.Segment(result, 0.5, 1.4));
        double centroidIn = TestSignals.SpectralCentroid(TestSignals.Segment(input, 0.5, 1.4));
        double centroidOut = TestSignals.SpectralCentroid(TestSignals.Segment(result, 0.5, 1.4));
        output.WriteLine($"f0 {f0:F1} Hz, centroide {centroidIn:F0} → {centroidOut:F0} Hz");

        Assert.InRange(f0, 140 * 0.98, 140 * 1.02);
        Assert.True(centroidOut > centroidIn * 1.1, "los formantes deberían subir");
    }

    [Fact]
    public void Psola_robot_mode_produces_constant_pitch()
    {
        var psola = new PsolaPitchShifter(Rate, VoiceRange.Medium);
        psola.SetParameters(1.0, 1.0, 110);
        var result = TestSignals.ProcessInBlocks(psola, TestSignals.Glide(120, 200, 2.0));

        double early = TestSignals.MeasureF0(TestSignals.Segment(result, 0.4, 0.8));
        double late = TestSignals.MeasureF0(TestSignals.Segment(result, 1.5, 1.9));
        output.WriteLine($"robot: {early:F1} Hz / {late:F1} Hz");
        Assert.InRange(early, 107, 113);
        Assert.InRange(late, 107, 113);
    }

    [Fact]
    public void Psola_identity_reports_its_real_delay_on_noise()
    {
        // En tramos sordos PSOLA solapa ruido al 50 %: con tono y formantes neutros, la salida es la entrada retrasada.
        var psola = new PsolaPitchShifter(Rate, VoiceRange.Medium);
        var input = TestSignals.Noise(1.0, 0.2f);
        var result = TestSignals.ProcessInBlocks(psola, input);

        int lag = TestSignals.BestLag(TestSignals.Segment(input, 0.2, 0.8), TestSignals.Segment(result, 0.2, 0.8), 2000);
        output.WriteLine($"retardo medido {lag} muestras, reportado {psola.LatencySamples}");
        Assert.InRange(psola.LatencySamples, lag - 16, lag + 16);
        Assert.InRange(lag * 1000.0 / Rate, 5, 15);
    }

    [Theory]
    [InlineData(VoiceRange.High, 220)]
    [InlineData(VoiceRange.Medium, 130)]
    [InlineData(VoiceRange.Low, 90)]
    public void Psola_voiced_latency_is_about_two_periods_of_the_voice(VoiceRange range, double hz)
    {
        var psola = new PsolaPitchShifter(Rate, range);
        psola.SetParameters(DspMath.SemitonesToRatio(5), 1.0, 0);
        TestSignals.ProcessInBlocks(psola, TestSignals.Vowel(hz, 1.0));

        double period = Rate / hz;
        double latencyMs = psola.LatencySamples * 1000.0 / Rate;
        output.WriteLine($"{range} {hz} Hz: latencia {latencyMs:F1} ms ({psola.LatencySamples / period:F2} periodos)");
        Assert.InRange(psola.LatencySamples, 1.8 * period, 3.2 * period);
    }
}
