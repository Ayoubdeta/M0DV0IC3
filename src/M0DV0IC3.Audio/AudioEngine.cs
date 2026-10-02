using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Wasapi;
using M0DV0IC3.Dsp;

namespace M0DV0IC3.Audio;

public sealed record EngineOptions
{
    public required string InputDeviceId { get; init; }

    /// <summary>Normalmente "CABLE Input (VB-Audio Virtual Cable)".</summary>
    public required string OutputDeviceId { get; init; }

    /// <summary>Auriculares para escucharte y oír el soundboard (opcional).</summary>
    public string? MonitorDeviceId { get; init; }

    /// <summary>Modo exclusivo en micro y salida: la latencia más baja, pero bloquea esos dispositivos para otras apps.</summary>
    public bool Exclusive { get; init; }

    /// <summary>Usar IAudioClient3 (periodos de ~3 ms en modo compartido) cuando el driver lo admita.</summary>
    public bool PreferLowLatency { get; init; } = true;

    /// <summary>Margen anti-cortes de los buffers. Súbelo si oyes chasquidos.</summary>
    public double SafetyMarginMs { get; init; } = 2.0;
}

/// <summary>Desglose de la latencia que añade la app, en milisegundos.</summary>
public sealed record LatencyReport(double CaptureMs, double ProcessingMs, double BufferMs, double OutputMs)
{
    public double TotalMs => CaptureMs + ProcessingMs + BufferMs + OutputMs;
}

/// <summary>
/// Conecta el <see cref="VoicePipeline"/> a los dispositivos reales. El DSP se ejecuta dentro del callback
/// de captura, sin hilos intermedios, y el resultado pasa a cada salida por un ring buffer con compensación de deriva.
/// </summary>
public sealed class AudioEngine : IDisposable, ICaptureSink
{
    private const int RingCapacity = 1 << 14;

    private readonly float[] _cableScratch = new float[VoicePipeline.MaxBlockSize];
    private readonly float[] _monitorScratch = new float[VoicePipeline.MaxBlockSize];
    private readonly Lock _gate = new();

    private WasapiCaptureStream? _capture;
    private WasapiRenderStream? _cableOut;
    private WasapiRenderStream? _monitorOut;
    private SpscRingBuffer? _cableRing;
    private SpscRingBuffer? _monitorRing;
    private DriftCompensatedReader? _cableReader;
    private DriftCompensatedReader? _monitorReader;
    private int _faulted;

    public AudioEngine(VoicePipeline pipeline) => Pipeline = pipeline;

    public VoicePipeline Pipeline { get; }

    public bool IsRunning => _capture is not null;

    public StreamInfo? CaptureInfo => _capture?.Info;

    public StreamInfo? OutputInfo => _cableOut?.Info;

    public StreamInfo? MonitorInfo => _monitorOut?.Info;

    /// <summary>
    /// Cortes de audio (el buffer se quedó vacío) desde que se estabilizó: no cuenta el primer segundo y medio,
    /// mientras el micro y las salidas se sincronizan.
    /// </summary>
    public int Underruns => Steady(_cableReader) + Steady(_monitorReader);

    /// <summary>Cortes durante el arranque. Son normales y no se oyen como un fallo.</summary>
    public int StartupUnderruns => (_cableReader?.WarmupUnderruns ?? 0) + (_monitorReader?.WarmupUnderruns ?? 0);

    /// <summary>Un dispositivo ha fallado mientras sonaba. Se lanza en un hilo de audio y el motor ya está parado.</summary>
    public event EventHandler<Exception>? Faulted;

    public void Start(EngineOptions options)
    {
        lock (_gate)
        {
            StopCore();
            _faulted = 0;
            int safety = (int)Math.Round(options.SafetyMarginMs * DspMath.SampleRate / 1000);

            try
            {
                _cableRing = new SpscRingBuffer(RingCapacity);
                _cableReader = new DriftCompensatedReader(_cableRing, safety);
                _cableOut = new WasapiRenderStream(options.OutputDeviceId, _cableReader, safety, options.Exclusive, options.PreferLowLatency);
                _cableOut.Faulted += OnStreamFaulted;
                StartStream(_cableOut, "la salida (micrófono virtual)");

                if (!string.IsNullOrEmpty(options.MonitorDeviceId) && options.MonitorDeviceId != options.OutputDeviceId)
                {
                    _monitorRing = new SpscRingBuffer(RingCapacity);
                    _monitorReader = new DriftCompensatedReader(_monitorRing, safety);
                    // El monitor va siempre en compartido: los auriculares suelen usarse a la vez para el juego o Discord.
                    _monitorOut = new WasapiRenderStream(options.MonitorDeviceId, _monitorReader, safety, exclusive: false, options.PreferLowLatency);
                    _monitorOut.Faulted += OnStreamFaulted;
                    StartStream(_monitorOut, "los auriculares");
                }

                _capture = new WasapiCaptureStream(options.InputDeviceId, this, options.Exclusive, options.PreferLowLatency);
                _capture.Faulted += OnStreamFaulted;
                StartStream(_capture, "el micrófono");
            }
            catch
            {
                StopCore();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    public LatencyReport GetLatency()
    {
        double perMs = DspMath.SampleRate / 1000.0;
        return new LatencyReport(
            CaptureMs: _capture?.Info?.PeriodMs ?? 0,
            ProcessingMs: Pipeline.LatencySamples / perMs,
            BufferMs: _cableReader?.AverageWaitMs ?? 0,
            OutputMs: (_cableOut?.TargetPaddingFrames ?? 0) / perMs);
    }

    public void Dispose() => Stop();

    void ICaptureSink.OnCaptured(ReadOnlySpan<float> mono)
    {
        var cableRing = _cableRing;
        if (cableRing is null) return;
        var monitorRing = _monitorRing;

        for (int offset = 0; offset < mono.Length; offset += VoicePipeline.MaxBlockSize)
        {
            int n = Math.Min(VoicePipeline.MaxBlockSize, mono.Length - offset);
            var cable = _cableScratch.AsSpan(0, n);
            var monitor = monitorRing is null ? Span<float>.Empty : _monitorScratch.AsSpan(0, n);
            Pipeline.Process(mono.Slice(offset, n), cable, monitor);
            cableRing.Write(cable);
            monitorRing?.Write(monitor);
        }
    }

    private static int Steady(DriftCompensatedReader? reader) => reader is null ? 0 : reader.Underruns - reader.WarmupUnderruns;

    private static void StartStream(WasapiStream stream, string what)
    {
        try
        {
            stream.Start();
        }
        catch (Exception ex)
        {
            throw new AudioDeviceException($"No se pudo abrir {what}: {ex.Message}", ex);
        }
    }

    private void OnStreamFaulted(WasapiStream stream, Exception error)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0) return;
        // No se puede hacer Join del propio hilo que ha fallado: se para todo en otro hilo.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Stop();
            Faulted?.Invoke(this, new AudioDeviceException($"Se perdió el dispositivo \"{stream.Info?.DeviceName}\": {error.Message}", error));
        });
    }

    private void StopCore()
    {
        _capture?.Dispose();
        _cableOut?.Dispose();
        _monitorOut?.Dispose();
        _capture = null;
        _cableOut = null;
        _monitorOut = null;
        _cableRing = _monitorRing = null;
        _cableReader = _monitorReader = null;
    }
}

public sealed class AudioDeviceException(string message, Exception inner) : Exception(message, inner);
