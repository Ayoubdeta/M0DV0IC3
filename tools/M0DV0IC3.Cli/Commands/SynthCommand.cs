using M0DV0IC3.Dsp;

namespace M0DV0IC3.Cli.Commands;

internal static class SynthCommand
{
    public static int Run(string[] args)
    {
        var a = CliArgs.Parse(args, ["seconds", "f0"], []);
        string output = a.Required(0, "el archivo de salida (.wav)");
        a.ExpectPositional(1);
        double seconds = a.GetDouble("seconds", 8, 0.5, 120);
        double f0 = a.GetDouble("f0", 130, 60, 500);

        var signal = SpeechSynth.Generate(seconds, f0);
        AudioFiles.WriteFloatWav(output, signal);

        double low = f0 * (1 - SpeechSynth.MaxPitchDeviation), high = f0 * (1 + SpeechSynth.MaxPitchDeviation);
        Console.WriteLine($"Señal de voz sintética: {seconds:0.##} s, 48 kHz mono float, f0 {f0:0.#} Hz (entre {low:0} y {high:0} Hz)");
        Console.WriteLine($"Pico: {AudioFiles.Db(DspMath.Peak(signal))} dBFS · RMS: {AudioFiles.Db(DspMath.Rms(signal))} dBFS");
        Console.WriteLine($"Guardada en {Path.GetFullPath(output)}");
        return 0;
    }
}
