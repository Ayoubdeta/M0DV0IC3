using System.Windows.Threading;
using M0DV0IC3.App.Localization;
using M0DV0IC3.Audio.AppAudio;
using M0DV0IC3.Audio;

namespace M0DV0IC3.App.Services;

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Error,
}

/// <summary>
/// Un único <see cref="VoicePipeline"/> y <see cref="AudioEngine"/> para toda la vida de la app.
/// Los arranques se agrupan (300 ms) y se hacen de uno en uno fuera del hilo de la UI.
/// Créalo y úsalo desde el hilo de la UI.
/// </summary>
public sealed class AudioService : IDisposable
{
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _restartTimer;
    private readonly DispatcherTimer _retryTimer;
    private EngineOptions? _requested;
    private Task? _inflight;
    private bool _restartRunning;
    private bool _restartAgain;
    private bool _disposed;

    public AudioService()
    {
        Pipeline = new VoicePipeline();
        Engine = new AudioEngine(Pipeline);
        AppAudio = new AppAudioStreamer(Pipeline);
        Devices = new DeviceService();
        Engine.Faulted += OnEngineFaulted;

        _restartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _restartTimer.Tick += async (_, _) =>
        {
            _restartTimer.Stop();
            await RestartAsync();
        };

        // Tras un error se reintenta cada vez más espaciado (3, 6, 12… hasta 30 s): sirve para fallos pasajeros,
        // como otra app que tenía el modo exclusivo. Si el dispositivo se desenchufa, lo retoma DevicesChanged.
        _retryTimer = new DispatcherTimer { Interval = MinRetryDelay };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            if (Engine.IsRunning || _requested is null) return;
            var next = TimeSpan.FromTicks(Math.Min(_retryTimer.Interval.Ticks * 2, MaxRetryDelay.Ticks));
            Log.Info($"Reintentando el audio (si falla, otra vez en {next.TotalSeconds:0} s)");
            _retryTimer.Interval = next;
            StartRestartTimer();
        };
    }

    public VoicePipeline Pipeline { get; }

    public AudioEngine Engine { get; }

    /// <summary>"Música por el micro": va aparte del motor y no se reinicia con él.</summary>
    public AppAudioStreamer AppAudio { get; }

    public DeviceService Devices { get; }

    public EngineState State { get; private set; } = EngineState.Stopped;

    /// <summary>Último error al abrir los dispositivos (en español), o null.</summary>
    public string? Error { get; private set; }

    /// <summary>Opciones con las que debería estar funcionando el motor (null = parado a propósito).</summary>
    public EngineOptions? RequestedOptions => _requested;

    public event EventHandler? StateChanged;

    /// <summary>Arranca (o para, con null) el motor con estas opciones. Varios cambios seguidos se agrupan.</summary>
    public void RequestRestart(EngineOptions? options)
    {
        if (_disposed) return;
        _requested = options;
        _retryTimer.Stop();
        _retryTimer.Interval = MinRetryDelay;
        StartRestartTimer();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _restartTimer.Stop();
        _retryTimer.Stop();
        Engine.Faulted -= OnEngineFaulted;
        try
        {
            // Si había un arranque en marcha, se espera para no liberar el pipeline con el audio sonando.
            _inflight?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Ya se registró en TryStart.
        }
        AppAudio.Dispose();
        Engine.Dispose();
        Pipeline.Dispose();
        Devices.Dispose();
    }

    private async Task RestartAsync()
    {
        if (_restartRunning)
        {
            _restartAgain = true;
            return;
        }

        _restartRunning = true;
        try
        {
            do
            {
                _restartAgain = false;
                var options = _requested;
                if (options is null)
                {
                    _inflight = Task.Run(Engine.Stop);
                    await _inflight;
                    SetState(EngineState.Stopped, null);
                    continue;
                }

                SetState(EngineState.Starting, null);
                var start = Task.Run(() => TryStart(options));
                _inflight = start;
                string? error = await start;
                if (_disposed) return;
                SetState(error is null ? EngineState.Running : EngineState.Error, error);
                if (error is null)
                {
                    _retryTimer.Interval = MinRetryDelay;
                }
                else if (!_restartAgain)
                {
                    _retryTimer.Stop();
                    _retryTimer.Start();
                }
            }
            while (_restartAgain && !_disposed);
        }
        finally
        {
            _restartRunning = false;
        }
    }

    private void StartRestartTimer()
    {
        _restartTimer.Stop();
        _restartTimer.Start();
    }

    private string? TryStart(EngineOptions options)
    {
        try
        {
            Engine.Start(options);
            Log.Info($"Audio en marcha. Micro: {Engine.CaptureInfo}; salida: {Engine.OutputInfo}; auriculares: {Engine.MonitorInfo?.ToString() ?? "no"}");
            return null;
        }
        catch (AudioDeviceException ex)
        {
            Log.Warn("No se pudo arrancar el audio", ex);
            return ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Error inesperado al arrancar el audio", ex);
            return Loc.F("No se pudo arrancar el audio: {0}", ex.Message);
        }
    }

    private void SetState(EngineState state, string? error)
    {
        State = state;
        Error = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEngineFaulted(object? sender, Exception error)
    {
        // Llega en un hilo del pool y el motor ya está parado.
        Log.Warn("El motor de audio se ha parado por un error", error);
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            SetState(EngineState.Error, error.Message);
            _retryTimer.Stop();
            _retryTimer.Start();
        });
    }
}
