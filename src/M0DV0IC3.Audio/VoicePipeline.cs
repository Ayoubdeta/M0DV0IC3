using M0DV0IC3.Audio.NoiseSuppression;
using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Recording;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Audio.Speech;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.Audio;

/// <summary>
/// Todo el procesamiento, sin dispositivos (la app, la CLI y los tests usan el mismo):
/// <code>
/// micro → RNNoise (opcional) → puerta de ruido → voz → + soundboard + frases + música de una app → limitador → CABLE Input → grabación
///                                                └→ (voz si "Escucharme") + (sonidos y frases si se pide) → limitador → auriculares
/// </code>
/// La música no va a los auriculares (ya la oyes en la propia app), salvo en el karaoke.
/// <see cref="Process"/> se llama desde el hilo de captura y no reserva memoria.
/// </summary>
public sealed class VoicePipeline : IDisposable
{
    public const int MaxBlockSize = 8192;

    private readonly float[] _voice = new float[MaxBlockSize];
    private readonly float[] _sounds = new float[MaxBlockSize];
    private readonly float[] _speech = new float[MaxBlockSize];
    private readonly float[] _appAudio = new float[MaxBlockSize];
    private readonly RnNoiseEffect? _rnNoise;
    private readonly Limiter _cableLimiter = new(DspMath.SampleRate);
    private readonly Limiter _monitorLimiter = new(DspMath.SampleRate);
    private readonly PeakMeter _inputMeter = new();
    private readonly PeakMeter _outputMeter = new();
    private readonly PeakMeter _appAudioMeter = new();
    private IRenderSource? _appAudioSource;
    private volatile float _appAudioGain = 0.25f;
    private volatile bool _noiseSuppression;
    private volatile bool _muted;
    private volatile bool _monitorVoice;
    private volatile bool _monitorSounds = true;
    private volatile bool _appAudioToMonitor;
    private bool _rnNoiseActive;

    public VoicePipeline(bool loadNoiseSuppression = true)
    {
        if (loadNoiseSuppression)
        {
            _rnNoise = RnNoiseEffect.TryCreate(out string? error);
            NoiseSuppressionError = error;
        }
        else
        {
            NoiseSuppressionError = "Desactivada.";
        }
    }

    public VoiceProcessor Voice { get; } = new(DspMath.SampleRate, MaxBlockSize);

    public NoiseGate Gate { get; } = new(DspMath.SampleRate, NoiseGate.DisabledThresholdDb);

    public SoundboardMixer Soundboard { get; } = new();

    /// <summary>Texto a voz: las frases suenan por el micro con su propia copia de la voz.</summary>
    public SpeechPlayer Speech { get; } = new(MaxBlockSize);

    /// <summary>Graba lo que sale por el micrófono virtual.</summary>
    public MicRecorder Recorder { get; } = new();

    public bool NoiseSuppressionAvailable => _rnNoise is not null;

    /// <summary>Por qué no está disponible la supresión de ruido (null si lo está).</summary>
    public string? NoiseSuppressionError { get; }

    public bool NoiseSuppression
    {
        get => _noiseSuppression;
        set => _noiseSuppression = value;
    }

    /// <summary>
    /// Silencio total hacia el micrófono virtual y los auriculares (voz y sonidos). El micro se sigue
    /// procesando y midiendo, para que al quitarlo no haya saltos.
    /// </summary>
    public bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    /// <summary>"Escucharme": la voz modificada también va a los auriculares.</summary>
    public bool MonitorVoice
    {
        get => _monitorVoice;
        set => _monitorVoice = value;
    }

    /// <summary>Los sonidos del soundboard también van a los auriculares.</summary>
    public bool MonitorSounds
    {
        get => _monitorSounds;
        set => _monitorSounds = value;
    }

    /// <summary>Audio de otra app (música) que se mezcla hacia el micrófono virtual, o null. Lo cambia <c>AppAudioStreamer</c>.</summary>
    public IRenderSource? AppAudio
    {
        get => Volatile.Read(ref _appAudioSource);
        set => Volatile.Write(ref _appAudioSource, value);
    }

    /// <summary>La música de la app también va a los auriculares (modo karaoke: la oyes para cantar encima).</summary>
    public bool AppAudioToMonitor
    {
        get => _appAudioToMonitor;
        set => _appAudioToMonitor = value;
    }

    /// <summary>Volumen de la música de la app (ganancia lineal, 0..4).</summary>
    public float AppAudioGain
    {
        get => _appAudioGain;
        set => _appAudioGain = Math.Clamp(value, 0f, 4f);
    }

    /// <summary>Latencia de procesamiento actual (RNNoise + PSOLA), en muestras.</summary>
    public int LatencySamples => (_rnNoiseActive && _rnNoise is not null ? _rnNoise.LatencySamples : 0) + Voice.LatencySamples;

    public float ReadInputPeak() => _inputMeter.ReadAndReset();

    public float ReadOutputPeak() => _outputMeter.ReadAndReset();

    /// <summary>Pico de la música de la app (ya con su volumen) desde la última lectura.</summary>
    public float ReadAppAudioPeak() => _appAudioMeter.ReadAndReset();

    /// <param name="input">Micro en mono a 48 kHz.</param>
    /// <param name="cableOut">Salida hacia el micrófono virtual (misma longitud que la entrada).</param>
    /// <param name="monitorOut">Salida hacia los auriculares, o vacío si no hay monitor.</param>
    public void Process(ReadOnlySpan<float> input, Span<float> cableOut, Span<float> monitorOut)
    {
        for (int offset = 0; offset < input.Length; offset += MaxBlockSize)
        {
            int n = Math.Min(MaxBlockSize, input.Length - offset);
            ProcessBlock(
                input.Slice(offset, n),
                cableOut.Slice(offset, n),
                monitorOut.IsEmpty ? Span<float>.Empty : monitorOut.Slice(offset, n));
        }
    }

    public void Dispose()
    {
        Recorder.Dispose();
        _rnNoise?.Dispose();
    }

    private void ProcessBlock(ReadOnlySpan<float> input, Span<float> cable, Span<float> monitor)
    {
        int n = input.Length;
        var voice = _voice.AsSpan(0, n);
        input.CopyTo(voice);
        _inputMeter.Update(DspMath.Peak(input));

        bool useRnNoise = _noiseSuppression && _rnNoise is not null;
        if (useRnNoise != _rnNoiseActive)
        {
            _rnNoiseActive = useRnNoise;
            if (useRnNoise) _rnNoise!.Reset();
        }
        if (useRnNoise) _rnNoise!.Process(voice);

        Gate.Process(voice);
        Voice.Process(voice);

        var sounds = _sounds.AsSpan(0, n);
        bool hasSounds = Soundboard.Render(sounds);
        var speech = _speech.AsSpan(0, n);
        bool hasSpeech = Speech.Render(speech);

        voice.CopyTo(cable);
        if (hasSounds)
        {
            for (int i = 0; i < n; i++) cable[i] += sounds[i];
        }
        if (hasSpeech)
        {
            for (int i = 0; i < n; i++) cable[i] += speech[i];
        }

        var appSource = AppAudio;
        var app = _appAudio.AsSpan(0, n);
        if (appSource is not null)
        {
            appSource.Render(app);
            float gain = _appAudioGain;
            for (int i = 0; i < n; i++) app[i] *= gain;
            _appAudioMeter.Update(DspMath.Peak(app));
            for (int i = 0; i < n; i++) cable[i] += app[i];
        }
        _cableLimiter.Process(cable);

        bool muted = _muted;
        if (muted) cable.Clear();
        _outputMeter.Update(DspMath.Peak(cable));
        Recorder.Write(cable);

        if (monitor.IsEmpty) return;
        if (muted)
        {
            monitor.Clear();
            return;
        }

        bool withVoice = _monitorVoice;
        bool withSounds = hasSounds && _monitorSounds;
        bool withSpeech = hasSpeech && _monitorSounds;
        bool withMusic = appSource is not null && _appAudioToMonitor;
        for (int i = 0; i < n; i++)
        {
            monitor[i] = (withVoice ? voice[i] : 0f) + (withSounds ? sounds[i] : 0f) + (withSpeech ? speech[i] : 0f)
                + (withMusic ? app[i] : 0f);
        }
        _monitorLimiter.Process(monitor);
    }
}
