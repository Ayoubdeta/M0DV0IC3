using System.Diagnostics;
using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Wasapi;
using M0DV0IC3.Dsp;
using NAudio.CoreAudioApi;

namespace M0DV0IC3.Cli.Commands;

/// <summary>
/// Abre los streams WASAPI de verdad y mide sus callbacks. Privacidad y seguridad:
/// lo capturado solo se usa para contar paquetes y medir el pico (nunca se guarda ni se reenvía),
/// y la salida reproduce únicamente silencio.
/// </summary>
internal static class ProbeCommand
{
    private const double SafetyMarginMs = 2.0;

    public static int Run(string[] args)
    {
        var a = CliArgs.Parse(args, ["input", "output", "seconds"], ["exclusive", "no-lowlatency"]);
        a.ExpectPositional(0);
        string inputSpec = a.Get("input", "default");
        string outputSpec = a.Get("output", "none");
        double seconds = a.GetDouble("seconds", 3, 0.5, 60);
        bool exclusive = a.Has("exclusive");
        bool lowLatency = !a.Has("no-lowlatency");

        var (inputId, inputName) = Resolve(DataFlow.Capture, inputSpec);
        (string Id, string Name)? output = outputSpec.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : Resolve(DataFlow.Render, outputSpec);

        Console.WriteLine($"Captura: {inputName}");
        Console.WriteLine($"Salida:  {(output is null ? "ninguna" : $"{output.Value.Name} (solo silencio)")}");
        Console.WriteLine($"Modo pedido: {(exclusive ? "exclusivo" : "compartido")}, baja latencia {(lowLatency ? "sí" : "no")} · {seconds:0.#} s");
        Console.WriteLine();

        var sink = new CaptureStatsSink();
        var silence = new SilenceSource();
        var faults = new List<string>();
        WasapiRenderStream? render = null;
        var capture = new WasapiCaptureStream(inputId, sink, exclusive, lowLatency);
        bool ok = true;
        try
        {
            if (output is not null)
            {
                int safetyFrames = (int)Math.Round(SafetyMarginMs * DspMath.SampleRate / 1000);
                render = new WasapiRenderStream(output.Value.Id, silence, safetyFrames, exclusive, lowLatency);
                render.Faulted += (_, ex) => { lock (faults) faults.Add($"salida: {ex.Message}"); };
                ok &= TryStart(render, "la salida");
            }
            capture.Faulted += (_, ex) => { lock (faults) faults.Add($"captura: {ex.Message}"); };
            ok &= TryStart(capture, "la captura");

            if (capture.Info is not null || render?.Info is not null)
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
        finally
        {
            capture.Dispose();
            render?.Dispose();
        }

        if (capture.Info is not null)
        {
            PrintInfo("Captura", capture.Info);
            sink.Stats.Print();
            string silent = sink.SilentPackets > 0 ? $" · paquetes en silencio digital: {sink.SilentPackets} de {sink.Stats.Callbacks}" : "";
            Console.WriteLine($"    Pico:      {AudioFiles.Db(sink.Peak)} dBFS{silent}");
            if (sink.Stats.Callbacks > 0 && sink.SilentPackets == sink.Stats.Callbacks)
                Console.WriteLine("    Todo es silencio digital: ¿micro silenciado o bloqueado en Privacidad > Micrófono?");
            Console.WriteLine();
        }
        if (render?.Info is not null)
        {
            PrintInfo("Salida", render.Info);
            Console.WriteLine($"    Relleno objetivo: {render.TargetPaddingFrames} muestras ({render.TargetPaddingFrames * 1000.0 / render.Info.SampleRate:0.00} ms)");
            silence.Stats.Print();
            Console.WriteLine();
        }
        foreach (string fault in faults)
        {
            Console.WriteLine($"Error durante la prueba ({fault})");
            ok = false;
        }
        return ok ? 0 : 1;
    }

    private static bool TryStart(WasapiStream stream, string what)
    {
        try
        {
            stream.Start();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo abrir {what}: {ex.Message} ({ex.GetType().Name}, 0x{ex.HResult:X8})");
            Console.WriteLine();
            return false;
        }
    }

    private static void PrintInfo(string title, StreamInfo info)
    {
        Console.WriteLine($"{title}: {info.DeviceName}");
        Console.WriteLine($"    Modo:      {info.ModeDescription} · {info.Format} · {info.SampleRate} Hz");
        Console.WriteLine($"    Periodo:   {info.PeriodFrames} muestras ({info.PeriodMs:0.00} ms) · buffer {info.BufferFrames} muestras" +
            $" ({info.BufferFrames * 1000.0 / info.SampleRate:0.00} ms)");
    }

    /// <summary><c>default</c>, el ID exacto o una parte única del nombre.</summary>
    internal static (string Id, string Name) Resolve(DataFlow flow, string spec)
    {
        string kind = flow == DataFlow.Capture ? "captura" : "salida";
        using var enumerator = new MMDeviceEnumerator();
        if (spec.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (!enumerator.TryGetDefaultAudioEndpoint(flow, Role.Console, out var device))
                throw new CliException($"No hay dispositivo de {kind} predeterminado.");
            using (device) return (device.ID, device.FriendlyName);
        }

        var devices = new List<(string Id, string Name)>();
        using (var collection = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            foreach (var device in collection)
            {
                using (device) devices.Add((device.ID, device.FriendlyName));
            }
        }

        foreach (var device in devices)
        {
            if (device.Id.Equals(spec, StringComparison.OrdinalIgnoreCase)) return device;
        }
        var matches = devices.Where(d => d.Name.Contains(spec, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliException($"No hay ningún dispositivo de {kind} activo con ID o nombre '{spec}'. Usa 'devices' para verlos."),
            _ => throw new CliException($"'{spec}' coincide con varios dispositivos de {kind}: {string.Join("; ", matches.Select(m => m.Name))}"),
        };
    }

    /// <summary>Estadísticas de los callbacks de un stream. Se escriben en el hilo de audio y se leen después de pararlo.</summary>
    private sealed class CallbackStats
    {
        private const int MaxTrackedSize = 8192;

        private readonly int[] _sizes = new int[MaxTrackedSize + 1];
        private long _first;
        private long _last;
        private long _firstFrames;
        private long _maxInterval;
        private long _minInterval = long.MaxValue;

        public long Callbacks { get; private set; }

        public long Frames { get; private set; }

        public void Record(int frames)
        {
            long now = Stopwatch.GetTimestamp();
            if (Callbacks == 0)
            {
                _first = now;
                _firstFrames = frames;
            }
            else
            {
                long interval = now - _last;
                _maxInterval = Math.Max(_maxInterval, interval);
                _minInterval = Math.Min(_minInterval, interval);
            }
            _last = now;
            Callbacks++;
            Frames += frames;
            _sizes[Math.Min(frames, MaxTrackedSize)]++;
        }

        public void Print()
        {
            if (Callbacks == 0)
            {
                Console.WriteLine("    Callbacks: ninguno");
                return;
            }
            double span = (double)(_last - _first) / Stopwatch.Frequency;
            string rate = span > 0 ? $" · tasa efectiva {(Frames - _firstFrames) / span:0} Hz" : "";
            Console.WriteLine($"    Callbacks: {Callbacks} · {Frames} muestras{rate}");

            var histogram = Enumerable.Range(0, _sizes.Length)
                .Where(size => _sizes[size] > 0)
                .OrderByDescending(size => _sizes[size])
                .Take(8)
                .Select(size => $"{(size == MaxTrackedSize ? $"≥{size}" : size.ToString())} ×{_sizes[size]}");
            Console.WriteLine($"    Tamaños:   {string.Join(", ", histogram)}");

            if (Callbacks > 1)
            {
                double toMs = 1000.0 / Stopwatch.Frequency;
                Console.WriteLine($"    Intervalo: media {(_last - _first) * toMs / (Callbacks - 1):0.00} ms · mín {_minInterval * toMs:0.00} · máx {_maxInterval * toMs:0.00} ms");
            }
        }
    }

    /// <summary>Solo mide: no copia ni guarda el audio capturado.</summary>
    private sealed class CaptureStatsSink : ICaptureSink
    {
        public CallbackStats Stats { get; } = new();

        public float Peak { get; private set; }

        public long SilentPackets { get; private set; }

        public void OnCaptured(ReadOnlySpan<float> mono)
        {
            Stats.Record(mono.Length);
            float peak = DspMath.Peak(mono);
            if (peak == 0f) SilentPackets++;
            if (peak > Peak) Peak = peak;
        }
    }

    /// <summary>Fuente de salida que solo entrega ceros.</summary>
    private sealed class SilenceSource : IRenderSource
    {
        public CallbackStats Stats { get; } = new();

        public void Render(Span<float> destination)
        {
            destination.Clear();
            Stats.Record(destination.Length);
        }
    }
}
