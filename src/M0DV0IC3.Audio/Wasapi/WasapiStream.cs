using M0DV0IC3.Audio.Realtime;
using NAudio.CoreAudioApi;

namespace M0DV0IC3.Audio.Wasapi;

/// <summary>
/// Base de los streams WASAPI event-driven. Cada stream tiene su propio hilo (MTA, con MMCSS "Pro Audio"),
/// y en ese hilo se crean, usan y liberan todos los objetos COM.
/// </summary>
public abstract class WasapiStream : IDisposable
{
    private readonly ManualResetEventSlim _initialized = new();
    private Thread? _thread;
    private Exception? _initError;
    private volatile bool _stopRequested;

    protected WasapiStream(string deviceId, bool exclusive, bool preferLowLatency)
    {
        DeviceId = deviceId;
        Exclusive = exclusive;
        PreferLowLatency = preferLowLatency;
    }

    public string DeviceId { get; }

    public bool Exclusive { get; }

    public bool PreferLowLatency { get; }

    public StreamInfo? Info { get; private set; }

    /// <summary>Error después de arrancar (por ejemplo, se ha desconectado el dispositivo). Se lanza en el hilo de audio.</summary>
    public event Action<WasapiStream, Exception>? Faulted;

    protected bool StopRequested => _stopRequested;

    protected abstract bool IsCapture { get; }

    /// <summary>
    /// Arranca el hilo y espera a que el dispositivo quede abierto (5 s como mucho). Si no se puede abrir, lanza la excepción.
    /// </summary>
    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("El stream ya está arrancado.");
        _stopRequested = false;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = IsCapture ? "M0DV0IC3 captura" : "M0DV0IC3 salida",
        };
        _thread.Start();
        if (!_initialized.Wait(TimeSpan.FromSeconds(5)))
        {
            // Un driver colgado no debe bloquear la app: el hilo saldrá solo en cuanto vuelva y vea la parada.
            _stopRequested = true;
            _thread = null;
            throw new TimeoutException(AudioText.T("El dispositivo no responde: lleva más de 5 s sin abrirse."));
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

    /// <summary>
    /// Bucle de audio, una vez abierto el cliente. Debe volver cuando <see cref="StopRequested"/> sea true.
    /// <paramref name="mode"/> es el modo que se ha conseguido, que puede no ser el pedido (exclusivo → compartido).
    /// </summary>
    private protected abstract void RunLoop(AudioClient client, SampleLayout layout, AutoResetEvent bufferReady, int periodFrames, StreamMode mode);

    private void Run()
    {
        IntPtr mmcss = Mmcss.Enter();
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        InitializedClient? opened = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDevice(DeviceId);
            opened = WasapiInitializer.Open(device, Exclusive, PreferLowLatency, IsCapture);

            using var bufferReady = new AutoResetEvent(false);
            opened.Client.SetEventHandle(bufferReady.SafeWaitHandle.DangerousGetHandle());
            Info = new StreamInfo(
                device.FriendlyName,
                opened.Mode,
                WasapiInitializer.SampleRate,
                opened.Layout.ToString(),
                opened.PeriodFrames,
                opened.Client.BufferSize);
            _initialized.Set();

            if (!_stopRequested) RunLoop(opened.Client, opened.Layout, bufferReady, opened.PeriodFrames, opened.Mode);
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
            try { opened?.Client.Stop(); } catch { /* el dispositivo puede haber desaparecido */ }
            opened?.Client.Dispose();
            device?.Dispose();
            enumerator?.Dispose();
            Mmcss.Leave(mmcss);
        }
    }
}

/// <summary>Recibe el audio capturado, ya en mono float a 48 kHz, en el hilo de captura.</summary>
public interface ICaptureSink
{
    void OnCaptured(ReadOnlySpan<float> mono);
}

public sealed class WasapiCaptureStream : WasapiStream
{
    private readonly ICaptureSink _sink;

    public WasapiCaptureStream(string deviceId, ICaptureSink sink, bool exclusive = false, bool preferLowLatency = true)
        : base(deviceId, exclusive, preferLowLatency) => _sink = sink;

    protected override bool IsCapture => true;

    private protected override void RunLoop(AudioClient client, SampleLayout layout, AutoResetEvent bufferReady, int periodFrames, StreamMode mode)
    {
        var capture = client.AudioCaptureClient;
        var mono = new float[Math.Max(client.BufferSize, periodFrames) * 2];
        client.Start();

        while (!StopRequested)
        {
            if (!bufferReady.WaitOne(200)) continue;
            while (!StopRequested && capture.GetNextPacketSize() > 0)
            {
                IntPtr data = capture.GetBuffer(out int frames, out var flags);
                var block = mono.AsSpan(0, Math.Min(frames, mono.Length));
                if ((flags & AudioClientBufferFlags.Silent) != 0) block.Clear();
                else SampleConverter.ToMono(data, block.Length, layout, block);
                capture.ReleaseBuffer(frames);
                _sink.OnCaptured(block);
            }
        }
    }
}

public sealed class WasapiRenderStream : WasapiStream
{
    private readonly IRenderSource _source;
    private readonly int _safetyFrames;

    public WasapiRenderStream(string deviceId, IRenderSource source, int safetyFrames, bool exclusive = false, bool preferLowLatency = true)
        : base(deviceId, exclusive, preferLowLatency)
    {
        _source = source;
        _safetyFrames = Math.Max(0, safetyFrames);
    }

    /// <summary>Muestras que se mantienen en el buffer del dispositivo (latencia de salida).</summary>
    public int TargetPaddingFrames { get; private set; }

    protected override bool IsCapture => false;

    private protected override void RunLoop(AudioClient client, SampleLayout layout, AutoResetEvent bufferReady, int periodFrames, StreamMode mode)
    {
        var render = client.AudioRenderClient;
        int bufferFrames = client.BufferSize;
        bool exclusive = mode == StreamMode.Exclusive;

        // En modo compartido no llenamos todo el buffer: basta un periodo más el margen. Cuanto menos
        // audio esperando en el buffer, menos latencia. En exclusivo hay que entregar el buffer entero en cada evento.
        int target = exclusive ? bufferFrames : Math.Min(bufferFrames, periodFrames + _safetyFrames);
        TargetPaddingFrames = target;
        var mono = new float[bufferFrames];

        render.GetBuffer(target);
        render.ReleaseBuffer(target, AudioClientBufferFlags.Silent);
        client.Start();

        while (!StopRequested)
        {
            if (!bufferReady.WaitOne(200)) continue;

            int frames = exclusive ? bufferFrames : target - client.CurrentPadding;
            if (frames <= 0) continue;

            var block = mono.AsSpan(0, frames);
            _source.Render(block);
            IntPtr buffer = render.GetBuffer(frames);
            SampleConverter.FromMono(block, buffer, frames, layout);
            render.ReleaseBuffer(frames, AudioClientBufferFlags.None);
        }
    }
}
