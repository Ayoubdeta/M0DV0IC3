using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Effects;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>Qué se le hace a la canción antes de mandarla.</summary>
public enum SongProcessing
{
    None,

    /// <summary>Modo karaoke: sin la voz del cantante.</summary>
    RemoveVocals,

    /// <summary>Con otra voz para el cantante (<see cref="SongVoiceChanger"/>).</summary>
    ChangeVoice,
}

/// <summary>
/// "Música por el micro": lo que suena en una app (Spotify, el navegador...) se mezcla con tu voz hacia el micrófono
/// virtual. Va aparte del motor: arrancarla o pararla no corta tu voz, y sigue puesta aunque el motor se reinicie.
/// La captura llega por un ring buffer con compensación de deriva, igual que la voz hacia las salidas.
/// </summary>
public sealed class AppAudioStreamer : IDisposable
{
    private const int RingCapacity = 1 << 15;

    // Algo más de margen que la voz: la app llega en paquetes de 10 ms con el reloj de otro dispositivo,
    // y unos milisegundos más de retraso en la música no se notan.
    private const double SafetyMs = 12;

    private readonly VoicePipeline _pipeline;
    private readonly Lock _gate = new();
    private readonly VocalRemover _remover = new();
    private AppAudioCapture? _capture;
    private SongProcessing _processing;
    private float _gain = 1f;

    public AppAudioStreamer(VoicePipeline pipeline) => _pipeline = pipeline;

    /// <summary>Hace falta Windows 10 versión 2004 o posterior.</summary>
    public static bool IsSupported => AppAudioCapture.IsSupported;

    /// <summary>La app que se está transmitiendo, o null.</summary>
    public AudioApp? Current { get; private set; }

    public bool IsRunning => Volatile.Read(ref _capture) is not null;

    /// <summary>Quitar la voz de la canción (karaoke) o cambiársela al cantante. Vale también para capturas futuras.</summary>
    public SongProcessing Processing
    {
        get => _processing;
        set
        {
            lock (_gate)
            {
                _processing = value;
                if (_capture is { } capture) capture.Processing = value;
            }
        }
    }

    /// <summary>La voz que se le pone al cantante con <see cref="SongProcessing.ChangeVoice"/>.</summary>
    public SongVoiceChanger SongVoice { get; } = new();

    /// <summary>Cuánta voz se quita en el modo karaoke (0..1).</summary>
    public float VocalRemovalStrength
    {
        get => _remover.Strength;
        set => _remover.Strength = value;
    }

    /// <summary>Ganancia de la captura: compensa que el karaoke baja el volumen de Spotify en Windows.</summary>
    public float CaptureGain
    {
        get => _gain;
        set
        {
            lock (_gate)
            {
                _gain = value;
                if (_capture is { } capture) capture.Gain = value;
            }
        }
    }

    /// <summary>La captura ha fallado mientras sonaba (ya está parada). Se lanza en un hilo del pool.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Empieza a transmitir esta app (si había otra, la cambia). Lanza <see cref="AudioDeviceException"/> si no se puede.</summary>
    public void Start(AudioApp app)
    {
        lock (_gate)
        {
            StopCore();
            var ring = new SpscRingBuffer(RingCapacity);
            var reader = new DriftCompensatedReader(ring, (int)(SafetyMs * DspMath.SampleRate / 1000));
            var capture = new AppAudioCapture(app.ProcessId, ring, _remover, SongVoice) { Processing = _processing, Gain = _gain };
            capture.Faulted += OnCaptureFaulted;
            try
            {
                capture.Start();
            }
            catch (Exception ex)
            {
                capture.Dispose();
                throw new AudioDeviceException($"No se pudo capturar el audio de {app.DisplayName}: {ex.Message}", ex);
            }
            Volatile.Write(ref _capture, capture);
            Current = app;
            _pipeline.AppAudio = reader;
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    public void Dispose() => Stop();

    private void StopCore()
    {
        _pipeline.AppAudio = null;
        _capture?.Dispose();
        Volatile.Write(ref _capture, null);
        Current = null;
    }

    private void OnCaptureFaulted(AppAudioCapture capture, Exception error)
    {
        // No se puede hacer Join del propio hilo que ha fallado: se para en otro hilo.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string? name;
            lock (_gate)
            {
                if (_capture != capture) return;
                name = Current?.DisplayName;
                StopCore();
            }
            Faulted?.Invoke(this, new AudioDeviceException($"Se dejó de capturar {name}: {error.Message}", error));
        });
    }
}
