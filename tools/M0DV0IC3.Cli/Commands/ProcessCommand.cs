using System.Diagnostics;
using M0DV0IC3.Audio;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.Cli.Commands;

internal static class ProcessCommand
{
    public static int Run(string[] args)
    {
        var a = CliArgs.Parse(args, ["voice", "range", "gate", "block"], ["denoise", "monitor"]);
        string inputPath = a.Required(0, "el archivo de entrada");
        string outputPath = a.Required(1, "el archivo de salida (.wav)");
        a.ExpectPositional(2);
        var voice = VoicesCommand.Find(a.Get("voice") ?? throw new CliException("Falta --voice <id|normal>.", 2));
        var range = a.GetRange();
        double? gateDb = a.Get("gate") is null ? null : a.GetDouble("gate", 0, -90, 0);
        int block = a.GetInt("block", 480, 1, VoicePipeline.MaxBlockSize);
        bool denoise = a.Has("denoise");
        bool monitor = a.Has("monitor");

        var input = AudioFiles.Load(inputPath);

        using var pipeline = CreatePipeline(voice, range, denoise, gateDb);
        pipeline.MonitorVoice = monitor;

        var cable = new float[input.Length];
        var monitorOut = monitor ? new float[input.Length] : null;
        var timing = ProcessInBlocks(pipeline, input, cable, monitorOut, block);

        AudioFiles.WriteFloatWav(outputPath, cable);
        string? monitorPath = null;
        if (monitorOut is not null)
        {
            monitorPath = Path.ChangeExtension(outputPath, null) + ".monitor.wav";
            AudioFiles.WriteFloatWav(monitorPath, monitorOut);
        }

        double seconds = AudioFiles.Seconds(input.Length);
        double blockMs = block * 1000.0 / DspMath.SampleRate;
        Console.WriteLine($"Entrada:   {inputPath} ({seconds:0.00} s)");
        Console.WriteLine($"Voz:       {voice.Id} ({voice.Name}){(voice.UsesPitch ? $" · PSOLA, rango {CliArgs.RangeName(range)}" : "")}");
        Console.WriteLine($"Bloque:    {block} muestras ({blockMs:0.0} ms)");
        Console.WriteLine($"RNNoise:   {(denoise ? "sí" : "no")} · puerta: {(gateDb is null ? "desactivada" : $"{gateDb:0.#} dB")}");
        double measured = MeasureDelayMs(input, cable);
        Console.WriteLine($"Latencia de procesamiento: {pipeline.LatencySamples * 1000.0 / DspMath.SampleRate:0.0} ms ({pipeline.LatencySamples} muestras)" +
            $" · medida en la salida (inicios de sílaba): {(double.IsNaN(measured) ? "-" : $"{measured:0.0} ms")}");
        Console.WriteLine($"Pico de salida: {AudioFiles.Db(DspMath.Peak(cable))} dBFS (entrada {AudioFiles.Db(DspMath.Peak(input))} dBFS)");
        Console.WriteLine($"Tiempo:    {timing.TotalMs:0.0} ms → {timing.TotalMs / (seconds * 10):0.00} % del tiempo real" +
            $" · bloque más lento {timing.MaxBlockMs:0.000} ms de {blockMs:0.0} ms (el primero, con JIT, {timing.FirstBlockMs:0.0} ms)");
        WarnIfUnhealthy(cable, "la salida");
        Console.WriteLine($"Salida:    {Path.GetFullPath(outputPath)}");
        if (monitorPath is not null) Console.WriteLine($"Monitor:   {Path.GetFullPath(monitorPath)}");
        return 0;
    }

    public static VoicePipeline CreatePipeline(VoicePreset voice, VoiceRange range, bool denoise, double? gateDb)
    {
        var pipeline = new VoicePipeline(loadNoiseSuppression: denoise);
        if (denoise && !pipeline.NoiseSuppressionAvailable)
        {
            string reason = pipeline.NoiseSuppressionError ?? "motivo desconocido";
            pipeline.Dispose();
            throw new CliException($"La supresión de ruido (RNNoise) no está disponible: {reason}");
        }
        pipeline.NoiseSuppression = denoise;
        pipeline.Gate.ThresholdDb = gateDb ?? NoiseGate.DisabledThresholdDb;
        // El rango se fija antes de elegir la voz: se aplica al crear la cadena.
        pipeline.Voice.Range = range;
        pipeline.Voice.SetPreset(voice);
        return pipeline;
    }

    /// <param name="FirstBlockMs">El primer bloque va aparte: en frío incluye el JIT.</param>
    /// <param name="MaxBlockMs">El bloque más lento sin contar el primero.</param>
    public readonly record struct Timing(double TotalMs, double FirstBlockMs, double MaxBlockMs);

    /// <summary>Procesa en bloques de <paramref name="block"/> muestras, como llegarían los paquetes de WASAPI.</summary>
    public static Timing ProcessInBlocks(VoicePipeline pipeline, ReadOnlySpan<float> input, Span<float> cable, Span<float> monitor, int block)
    {
        long firstTicks = 0, maxTicks = 0;
        long start = Stopwatch.GetTimestamp();
        for (int offset = 0; offset < input.Length; offset += block)
        {
            int n = Math.Min(block, input.Length - offset);
            long before = Stopwatch.GetTimestamp();
            pipeline.Process(input.Slice(offset, n), cable.Slice(offset, n), monitor.IsEmpty ? Span<float>.Empty : monitor.Slice(offset, n));
            long ticks = Stopwatch.GetTimestamp() - before;
            if (offset == 0) firstTicks = ticks;
            else maxTicks = Math.Max(maxTicks, ticks);
        }
        long total = Stopwatch.GetTimestamp() - start;
        double toMs = 1000.0 / Stopwatch.Frequency;
        return new Timing(total * toMs, firstTicks * toMs, maxTicks * toMs);
    }

    /// <summary>
    /// Retardo real de la salida: mediana, sobre los inicios de sílaba tras un silencio, de cuánto tarda la salida
    /// en cruzar el mismo umbral (compensado por la diferencia de nivel global). Funciona aunque cambie el tono,
    /// donde la forma de onda ya no se parece. Devuelve NaN si no hay al menos 3 inicios claros en entrada y salida
    /// (por ejemplo, con reverb o eco las pausas de la salida no quedan en silencio).
    /// </summary>
    public static double MeasureDelayMs(ReadOnlySpan<float> input, ReadOnlySpan<float> output, double maxMs = 100)
    {
        const int hop = 24; // 0,5 ms
        const int minQuietFrames = 40;
        var a = EnvelopeDb(input, hop);
        var b = EnvelopeDb(output, hop);
        double peakDb = DspMath.GainToDb(DspMath.Peak(input));
        double levelDifference = DspMath.GainToDb(DspMath.Rms(output)) - DspMath.GainToDb(DspMath.Rms(input));
        double quietDb = peakDb - 45, onsetDb = peakDb - 30;
        double outputQuietDb = quietDb + levelDifference, outputOnsetDb = onsetDb + levelDifference;
        int maxFrames = (int)(maxMs * DspMath.SampleRate / 1000 / hop);

        var delays = new List<int>();
        int quiet = 0, lastQuiet = 0;
        for (int f = 1; f < a.Length; f++)
        {
            if (a[f] < quietDb)
            {
                quiet++;
                lastQuiet = f;
                continue;
            }
            // Si cuando la entrada estaba en silencio la salida aún sonaba (cola de reverb o eco), ese inicio no sirve.
            if (a[f] >= onsetDb && a[f - 1] < onsetDb && quiet >= minQuietFrames && lastQuiet < b.Length && b[lastQuiet] < outputQuietDb)
            {
                for (int g = f; g < Math.Min(b.Length, f + maxFrames); g++)
                {
                    if (b[g] < outputOnsetDb) continue;
                    delays.Add(g - f);
                    break;
                }
            }
            if (a[f] >= onsetDb) quiet = 0;
        }
        if (delays.Count < 3) return double.NaN;
        delays.Sort();
        return delays[delays.Count / 2] * hop * 1000.0 / DspMath.SampleRate;
    }

    private static double[] EnvelopeDb(ReadOnlySpan<float> x, int hop)
    {
        var envelope = new double[x.Length / hop];
        for (int f = 0; f < envelope.Length; f++) envelope[f] = DspMath.GainToDb(DspMath.Rms(x.Slice(f * hop, hop)));
        return envelope;
    }

    public static void WarnIfUnhealthy(ReadOnlySpan<float> samples, string what)
    {
        var (nonFinite, over) = AudioFiles.Health(samples);
        if (nonFinite > 0) Console.WriteLine($"¡AVISO! {what} tiene {nonFinite} muestras NaN o infinitas.");
        if (over > 0) Console.WriteLine($"¡AVISO! {what} tiene {over} muestras por encima de 0 dBFS.");
    }
}
