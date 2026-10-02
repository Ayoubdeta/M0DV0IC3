using System.Diagnostics;
using M0DV0IC3.Audio;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.Cli.Commands;

internal static class BenchCommand
{
    private const int WarmupMinWallMs = 250;

    public static int Run(string[] args)
    {
        var a = CliArgs.Parse(args, ["seconds", "block", "range"], []);
        a.ExpectPositional(0);
        double seconds = a.GetDouble("seconds", 30, 2, 120);
        int block = a.GetInt("block", 480, 1, VoicePipeline.MaxBlockSize);
        var range = a.GetRange();

#if DEBUG
        Console.WriteLine("Aviso: build Debug. Para medir, usa: dotnet run -c Release --project tools/M0DV0IC3.Cli -- bench");
#endif
        var signal = SpeechSynth.Generate(seconds, 130);
        var warmup = signal.AsMemory(0, DspMath.SampleRate);
        var output = new float[signal.Length];

        var cases = VoicesCommand.AllVoices().Select(v => (Label: v.Id, Voice: v, Denoise: false)).ToList();
        string? denoiseNote = null;
        using (var probe = new VoicePipeline(loadNoiseSuppression: true))
        {
            if (probe.NoiseSuppressionAvailable) cases.Add(("mujer+rnnoise", VoicesCommand.Find("mujer"), true));
            else denoiseNote = $"RNNoise no disponible ({probe.NoiseSuppressionError}): se omite mujer+rnnoise.";
        }

        Console.WriteLine($"Señal sintética de {seconds:0.#} s (f0 130 Hz), bloques de {block} muestras ({block * 1000.0 / DspMath.SampleRate:0.0} ms), rango {CliArgs.RangeName(range)}");
        Console.WriteLine();

        double inputRms = DspMath.Rms(signal);
        var table = new TextTable("Voz", "PSOLA", "CPU ms/s", "Tiempo real", "Bloque máx (ms)", "Latencia (ms)", "Pico (dBFS)", "Nivel (dB)", "")
            .AlignRight(2, 3, 4, 5, 6, 7);
        // Como el hilo de audio de la app (que además usa MMCSS), para que el SO interrumpa lo menos posible.
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        foreach (var (label, voice, denoise) in cases)
        {
            // Calentamiento en un pipeline aparte: al menos 1 s de audio y 250 ms de reloj, para que el JIT
            // optimice (tiered compilation). Así el pipeline medido empieza de cero y el resultado es reproducible.
            using (var warm = ProcessCommand.CreatePipeline(voice, range, denoise, gateDb: null))
            {
                var watch = Stopwatch.StartNew();
                do ProcessCommand.ProcessInBlocks(warm, warmup.Span, output, Span<float>.Empty, block);
                while (watch.ElapsedMilliseconds < WarmupMinWallMs);
            }

            using var pipeline = ProcessCommand.CreatePipeline(voice, range, denoise, gateDb: null);
            GC.Collect();
            var timing = ProcessCommand.ProcessInBlocks(pipeline, signal, output, Span<float>.Empty, block);
            double cpuPerSecond = timing.TotalMs / seconds;
            var (nonFinite, over) = AudioFiles.Health(output);
            string health = nonFinite > 0 ? $"¡{nonFinite} NaN!" : over > 0 ? $"¡{over} > 0 dBFS!" : "";

            table.Add(
                label,
                voice.UsesPitch ? "sí" : "no",
                $"{cpuPerSecond:0.00}",
                $"{cpuPerSecond / 10:0.000} %",
                $"{Math.Max(timing.FirstBlockMs, timing.MaxBlockMs):0.000}",
                $"{pipeline.LatencySamples * 1000.0 / DspMath.SampleRate:0.0}",
                AudioFiles.Db(DspMath.Peak(output)),
                $"{DspMath.GainToDb(DspMath.Rms(output) / inputRms):+0.0;-0.0;0.0}",
                health);
        }
        table.Print();
        if (denoiseNote is not null) Console.WriteLine(denoiseNote);
        Console.WriteLine();
        Console.WriteLine("CPU ms/s: milisegundos de CPU por segundo de audio (un hilo). Tiempo real = CPU / duración del audio.");
        Console.WriteLine("Bloque máx: el bloque más lento; incluye interrupciones del sistema, así que varía entre ejecuciones.");
        Console.WriteLine("Nivel: RMS de la salida respecto de la entrada. Las voces incluidas deberían quedar en ±0,5 dB.");
        return 0;
    }
}
