using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Wasapi;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace M0DV0IC3.Audio.AppAudio;

/// <summary>
/// Captura lo que reproduce una app (y sus procesos hijos) con el "process loopback" de Windows 10 2004 o posterior.
/// Solo esa app: ni el resto del sistema ni Discord, que así no se oye a sí mismo. Entrega mono float a 48 kHz en
/// un <see cref="SpscRingBuffer"/>. Tiene su propio hilo (MTA, MMCSS "Audio"), donde vive el cliente COM.
/// Si la app está en silencio, Windows sigue entregando paquetes de silencio cada 10 ms.
/// </summary>
internal sealed class AppAudioCapture : IDisposable
{
    private const long BufferDuration = 20 * 10_000;

    private readonly uint _processId;
    private readonly SpscRingBuffer _ring;
    private readonly ManualResetEventSlim _initialized = new();
    private Thread? _thread;
    private Exception? _initError;
    private volatile bool _stopRequested;

    public AppAudioCapture(uint processId, SpscRingBuffer ring)
    {
        _processId = processId;
        _ring = ring;
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
            throw new TimeoutException("Windows no responde: la captura lleva más de 5 s sin abrirse.");
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
                throw new PlatformNotSupportedException("Hace falta Windows 10 versión 2004 o posterior.");

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
                        if (silent) block.Clear();
                        else SampleConverter.ToMono(data + done * layout.BlockAlign, block.Length, layout, block);
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
}
