using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Wasapi;
using M0DV0IC3.Dsp.Effects;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>
/// Captura lo que reproduce una app (y sus procesos hijos) con el "process loopback" de Windows 10 2004 o posterior.
/// Solo esa app: ni el resto del sistema ni Discord, que así no se oye a sí mismo. Entrega mono float a 48 kHz en
/// un <see cref="SpscRingBuffer"/>. Tiene su propio hilo (MTA, MMCSS "Audio"), donde vive el cliente COM.
/// Si la app está en silencio, Windows sigue entregando paquetes de silencio cada 10 ms.
/// <para>En el modo karaoke, el estéreo pasa antes por <see cref="VocalRemover"/>, y para cambiarle la voz al cantante,
/// por <see cref="SongVoiceChanger"/> (los dos necesitan los dos canales). Lo que se captura ya lleva aplicado el volumen
/// de la app en Windows: <see cref="Gain"/> lo compensa.</para>
/// </summary>
internal sealed class AppAudioCapture : IDisposable
{
    private const long BufferDuration = 20 * 10_000;

    private readonly uint _processId;
    private readonly SpscRingBuffer _ring;
    private readonly VocalRemover _remover;
    private readonly SongVoiceChanger _changer;
    private volatile SongProcessing _processing;
    private float _gain = 1f;
    private readonly ManualResetEventSlim _initialized = new();
    private Thread? _thread;
    private Exception? _initError;
    private volatile bool _stopRequested;

    public AppAudioCapture(uint processId, SpscRingBuffer ring, VocalRemover remover, SongVoiceChanger changer)
    {
        _processId = processId;
        _ring = ring;
        _remover = remover;
        _changer = changer;
    }

    /// <summary>Quitar la voz (karaoke) o cambiársela al cantante. Se puede cambiar mientras captura.</summary>
    public SongProcessing Processing
    {
        get => _processing;
        set => _processing = value;
    }

    /// <summary>Ganancia que se aplica a lo capturado (compensa el volumen bajado de la app).</summary>
    public float Gain
    {
        get => Volatile.Read(ref _gain);
        set => Volatile.Write(ref _gain, value);
    }

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    /// <summary>Error después de arrancar. Se lanza en el hilo de captura.</summary>
    public event Action<AppAudioCapture, Exception>? Faulted;

    /// <summary>Arranca el hilo y espera a que la captura quede abierta (5 s como mucho). Si no se puede abrir, lanza la excepción.</summary>
    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("La captura ya está arrancada.");
        _thread = new Thread(Run) { IsBackground = true, Name = "M0DV0IC3 música" };
        _thread.Start();
        if (!_initialized.Wait(TimeSpan.FromSeconds(5)))
        {
            _stopRequested = true;
            _thread = null;
            throw new TimeoutException(AudioText.T("Windows no responde: la captura lleva más de 5 s sin abrirse."));
        }
        if (_initError is not null)
        {
            _thread.Join();
            _thread = null;
            throw _initError;
        }
    }

    public void Stop()
    {
        _stopRequested = true;
        _thread?.Join(2000);
        _thread = null;
    }

    // _initialized no se libera a propósito: un hilo abandonado por el timeout aún podría usarlo.
    public void Dispose() => Stop();

    private void Run()
    {
        IntPtr mmcss = Mmcss.Enter("Audio");
        AudioClient? client = null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                throw new PlatformNotSupportedException(AudioText.T("Hace falta Windows 10 versión 2004 o posterior."));

            client = AudioClient.ActivateProcessLoopbackAsync(_processId, ProcessLoopbackMode.IncludeTargetProcessTree).GetAwaiter().GetResult();
            var format = WaveFormat.CreateIeeeFloatWaveFormat(WasapiInitializer.SampleRate, 2);
            client.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                BufferDuration, 0, format, Guid.Empty);

            using var ready = new AutoResetEvent(false);
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            var layout = SampleLayout.From(format);
            var capture = client.AudioCaptureClient;
            var mono = new float[WasapiInitializer.SampleRate / 10];
            var left = new float[mono.Length];
            var right = new float[mono.Length];
            bool stereoFloat = layout.Encoding == SampleEncoding.Float32 && layout.Channels == 2;
            _remover.Reset();
            _changer.Reset();
            client.Start();
            _initialized.Set();

            while (!_stopRequested)
            {
                if (!ready.WaitOne(200)) continue;
                while (!_stopRequested && capture.GetNextPacketSize() > 0)
                {
                    IntPtr data = capture.GetBuffer(out int frames, out var flags);
                    bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
                    for (int done = 0; done < frames; done += mono.Length)
                    {
                        var block = mono.AsSpan(0, Math.Min(mono.Length, frames - done));
                        var processing = _processing;
                        float gain = Gain;
                        if (processing != SongProcessing.None && stereoFloat)
                        {
                            // El filtro necesita seguir recibiendo audio (también el silencio) para no desfasarse.
                            var l = left.AsSpan(0, block.Length);
                            var r = right.AsSpan(0, block.Length);
                            if (silent)
                            {
                                l.Clear();
                                r.Clear();
                            }
                            else
                            {
                                Deinterleave(data + done * layout.BlockAlign, l, r);
                            }
                            // La compensación va antes de procesar: con la app bajada al 0,1 %, la voz del cantante
                            // llegaría 60 dB por debajo y el detector de tono la tomaría por silencio.
                            if (gain != 1f)
                            {
                                for (int i = 0; i < l.Length; i++)
                                {
                                    l[i] *= gain;
                                    r[i] *= gain;
                                }
                            }
                            if (processing == SongProcessing.RemoveVocals) _remover.Process(l, r, block);
                            else _changer.Process(l, r, block);
                        }
                        else
                        {
                            if (silent) block.Clear();
                            else SampleConverter.ToMono(data + done * layout.BlockAlign, block.Length, layout, block);
                            if (gain != 1f)
                            {
                                for (int i = 0; i < block.Length; i++) block[i] *= gain;
                            }
                        }
                        _ring.Write(block);
                    }
                    capture.ReleaseBuffer(frames);
                }
            }
        }
        catch (Exception ex)
        {
            if (!_initialized.IsSet)
            {
                _initError = ex;
                _initialized.Set();
            }
            else if (!_stopRequested)
            {
                Faulted?.Invoke(this, ex);
            }
        }
        finally
        {
            try { client?.Stop(); } catch { /* la app puede haberse cerrado */ }
            client?.Dispose();
            Mmcss.Leave(mmcss);
        }
    }

    private static unsafe void Deinterleave(IntPtr data, Span<float> left, Span<float> right)
    {
        float* samples = (float*)data;
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = samples[2 * i];
            right[i] = samples[2 * i + 1];
        }
    }
}
