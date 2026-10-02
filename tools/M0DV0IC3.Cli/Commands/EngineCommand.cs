using M0DV0IC3.Audio;
using M0DV0IC3.Audio.AppAudio;
using M0DV0IC3.Audio.Wasapi;
using M0DV0IC3.Dsp.Presets;
using NAudio.CoreAudioApi;

namespace M0DV0IC3.Cli.Commands;

/// <summary>
/// Arranca el motor completo de la app (micro → pipeline → ring buffers → salidas) con dispositivos reales
/// y mide su latencia y sus cortes. El pipeline va SIEMPRE silenciado: el audio recorre todo el camino
/// y se procesa, pero a las salidas solo llegan ceros, así que nunca suena nada.
/// </summary>
internal static class EngineCommand
{
    public static int Run(string[] args)
    {
        var a = CliArgs.Parse(args, ["input", "output", "monitor", "seconds", "voice", "margin", "app"], ["exclusive", "no-lowlatency", "denoise"]);
        a.ExpectPositional(0);
        var (inputId, inputName) = ProbeCommand.Resolve(DataFlow.Capture, a.Get("input", "default"));
        var (outputId, outputName) = ProbeCommand.Resolve(DataFlow.Render, a.Get("output", "default"));
        string monitorSpec = a.Get("monitor", "none");
        (string Id, string Name)? monitor = monitorSpec.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : ProbeCommand.Resolve(DataFlow.Render, monitorSpec);
        double seconds = a.GetDouble("seconds", 5, 1, 120);
        string voiceId = a.Get("voice", "mujer");
        var voice = BuiltInVoices.Find(voiceId) ?? throw new CliException($"Voz desconocida: '{voiceId}'.", 2);
        AudioApp? app = a.Get("app") is { } appSpec ? AppsCommand.Find(appSpec) : null;

        using var pipeline = new VoicePipeline();
        pipeline.Muted = true;
        pipeline.MonitorVoice = true;
        pipeline.NoiseSuppression = a.Has("denoise");
        pipeline.Voice.Range = a.GetRange();
        pipeline.Voice.SetPreset(voice);

        var options = new EngineOptions
        {
            InputDeviceId = inputId,
            OutputDeviceId = outputId,
            MonitorDeviceId = monitor?.Id,
            Exclusive = a.Has("exclusive"),
            PreferLowLatency = !a.Has("no-lowlatency"),
            SafetyMarginMs = a.GetDouble("margin", 2, 0, 50),
        };

        Console.WriteLine($"Micro:   {inputName}");
        Console.WriteLine($"Salida:  {outputName} (silenciada: solo ceros)");
        Console.WriteLine($"Monitor: {monitor?.Name ?? "ninguno"}");
        if (app is not null) Console.WriteLine($"Música:  {app.DisplayName} (pid {app.ProcessId}), mezclada a {AudioFiles.Db(pipeline.AppAudioGain)} dB");
        Console.WriteLine($"Voz: {voice.Name}{(pipeline.NoiseSuppression ? " + RNNoise" : "")} · {(options.Exclusive ? "exclusivo" : "compartido")} · margen {options.SafetyMarginMs} ms · {seconds:0.#} s");
        Console.WriteLine();

        using var engine = new AudioEngine(pipeline);
        Exception? fault = null;
        engine.Faulted += (_, ex) => Volatile.Write(ref fault, ex);
        try
        {
            engine.Start(options);
        }
        catch (AudioDeviceException ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }

        using var music = new AppAudioStreamer(pipeline);
        if (app is not null)
        {
            try
            {
                music.Start(app);
            }
            catch (AudioDeviceException ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }
        }

        Print("Micro", engine.CaptureInfo);
        Print("Salida", engine.OutputInfo);
        if (engine.MonitorInfo is not null) Print("Monitor", engine.MonitorInfo);
        Console.WriteLine();
        Console.WriteLine($"    t   captura  procesado  buffer  salida   TOTAL   pico micro  cortes{(app is null ? "" : "  pico música")}");

        double inputPeak = 0;
        for (int t = 1; t <= (int)Math.Ceiling(seconds) && Volatile.Read(ref fault) is null; t++)
        {
            Thread.Sleep(1000);
            var latency = engine.GetLatency();
            float peak = pipeline.ReadInputPeak();
            inputPeak = Math.Max(inputPeak, peak);
            string musicPeak = app is null ? "" : $"  {AudioFiles.Db(pipeline.ReadAppAudioPeak()),8} dB";
            Console.WriteLine($"  {t,3} s  {latency.CaptureMs,6:0.0}  {latency.ProcessingMs,8:0.0}  {latency.BufferMs,6:0.0}  {latency.OutputMs,6:0.0}  {latency.TotalMs,6:0.0} ms  {AudioFiles.Db(peak),8} dB  {engine.Underruns,5}{musicPeak}");
        }

        int underruns = engine.Underruns, startup = engine.StartupUnderruns;
        engine.Stop();
        Console.WriteLine();
        if (fault is not null)
        {
            Console.WriteLine($"El motor se paró por un error: {fault.Message}");
            return 1;
        }
        Console.WriteLine(inputPeak > 0
            ? $"OK: el motor ha funcionado {seconds:0.#} s con {underruns} cortes (y {startup} al arrancar, mientras se sincronizan los dispositivos)."
            : "El micro solo ha entregado silencio digital: ¿está silenciado o bloqueado en Privacidad > Micrófono?");
        return 0;
    }

    private static void Print(string title, StreamInfo? info)
    {
        if (info is null) return;
        Console.WriteLine($"{title,-8} {info.DeviceName}: {info.ModeDescription}, {info.Format}, periodo {info.PeriodMs:0.00} ms, buffer {info.BufferFrames} muestras");
    }
}
