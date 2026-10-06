using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

/// <summary>
/// La voz del cantante va afinada con la canción: el tono cambia en octavas (si no, la melodía pasa a otra tonalidad),
/// el autotune ajusta a todas las notas y el robot sigue la melodía en vez de cantar en una nota fija.
/// </summary>
public sealed class InTuneVoiceTests(ITestOutputHelper output)
{
    private static double Sing(VoicePreset preset, float[] voice, bool inTune)
    {
        var chain = new VoiceChain(preset, TestSignals.Rate, VoiceRange.Medium, new PitchProfile(), inTune);
        var result = TestSignals.ProcessInBlocks(chain, voice);
        double seconds = (double)voice.Length / TestSignals.Rate;
        return TestSignals.MeasureF0(TestSignals.Segment(result, seconds - 0.8, seconds - 0.1));
    }

    [Theory]
    [InlineData(8, 12)]
    [InlineData(5, 12)]
    [InlineData(-5, -12)]
    [InlineData(19, 24)]
    [InlineData(2, 0)]
    [InlineData(-2.5, 0)]
    public void Shifts_are_rounded_to_whole_octaves(double semitones, double expected) =>
        Assert.Equal(expected, PsolaPitchShifter.ToOctaves(semitones));

    [Theory]
    [InlineData(8, 400)]
    [InlineData(-5, 100)]
    [InlineData(2, 200)]
    public void The_singer_moves_in_octaves(double semitones, double expectedHz)
    {
        var preset = VoicePreset.Neutral with { Id = "prueba", PitchSemitones = semitones };
        var voice = TestSignals.Vowel(200, 1.5);
        double normal = Sing(preset, voice, inTune: false);
        double inTune = Sing(preset, voice, inTune: true);
        output.WriteLine($"{semitones:+0;-0} st a 200 Hz: {normal:F0} Hz con tu voz, {inTune:F0} Hz con la del cantante");
        Assert.InRange(inTune, expectedHz * 0.98, expectedHz * 1.02);
    }

    [Fact]
    public void Target_pitch_voices_also_move_in_octaves()
    {
        // Mujer con un cantante de 120 Hz: con tu voz iría a ~215 Hz (+10 st); con la del cantante, una octava.
        var woman = BuiltInVoices.All.First(v => v.Id == "mujer");
        var voice = TestSignals.Vowel(120, 2.0);
        double normal = Sing(woman, voice, inTune: false);
        double inTune = Sing(woman, voice, inTune: true);
        output.WriteLine($"Mujer con 120 Hz: {normal:F0} Hz con tu voz, {inTune:F0} Hz con la del cantante");
        Assert.InRange(inTune, 235, 245);
    }

    [Fact]
    public void Autotune_follows_every_note_of_the_song()
    {
        // Do#4 no está en La menor: el autotune de serie lo movería a Do o a Re; con la canción se queda.
        var autotune = BuiltInVoices.All.First(v => v.Id == "autotune");
        var voice = TestSignals.Vowel(277.18, 1.5);
        double normal = Sing(autotune, voice, inTune: false);
        double inTune = Sing(autotune, voice, inTune: true);
        output.WriteLine($"Do#4 (277 Hz) con Autotune: {normal:F0} Hz con tu voz, {inTune:F0} Hz con la del cantante");
        Assert.InRange(inTune, 272, 282);
    }

    [Fact]
    public void The_robot_sings_the_melody_instead_of_one_note()
    {
        var robot = BuiltInVoices.All.First(v => v.Id == "robot");
        var voice = TestSignals.Vowel(196, 1.5);
        double normal = Sing(robot, voice, inTune: false);
        double inTune = Sing(robot, voice, inTune: true);
        output.WriteLine($"Sol3 (196 Hz) con Robot: {normal:F0} Hz con tu voz, {inTune:F0} Hz con la del cantante");
        Assert.InRange(inTune, 192, 200);
    }
}
