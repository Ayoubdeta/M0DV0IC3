using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Dsp;
using NAudio.Wave;

namespace M0DV0IC3.Cli;

/// <summary>Lectura y escritura de archivos de audio, siempre en mono float a 48 kHz.</summary>
internal static class AudioFiles
{
    public static float[] Load(string path)
    {
        if (!File.Exists(path)) throw new CliException($"No existe el archivo: {path}");
        float[] samples;
        try
        {
            samples = SoundLoader.Load(path, "cli", Path.GetFileName(path)).Samples;
        }
        catch (Exception ex)
        {
            throw new CliException($"No se pudo leer '{path}': {ex.Message}");
        }

        if (samples.Length == 0) throw new CliException($"El archivo '{path}' no tiene audio.");
        if (samples.Length >= SoundLoader.MaxDuration.TotalSeconds * DspMath.SampleRate)
            Console.WriteLine($"Aviso: solo se ha leído el principio del archivo ({SoundLoader.MaxDuration.TotalMinutes:0} min).");
        return samples;
    }

    public static void WriteFloatWav(string path, float[] samples)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(DspMath.SampleRate, 1));
            writer.WriteSamples(samples, 0, samples.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliException($"No se pudo escribir '{path}': {ex.Message}");
        }
    }

    public static double Seconds(int samples) => (double)samples / DspMath.SampleRate;

    public static string Db(double gain) => gain <= 1e-9 ? "-inf" : $"{DspMath.GainToDb(gain):0.0}";

    /// <summary>Muestras no finitas (NaN o infinito) y muestras por encima de 0 dBFS.</summary>
    public static (int NonFinite, int OverFullScale) Health(ReadOnlySpan<float> samples)
    {
        int nonFinite = 0, over = 0;
        foreach (float x in samples)
        {
            if (!float.IsFinite(x)) nonFinite++;
            else if (Math.Abs(x) > 1f) over++;
        }
        return (nonFinite, over);
    }
}
