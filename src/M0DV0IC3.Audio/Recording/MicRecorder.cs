using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Dsp;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace M0DV0IC3.Audio.Recording;

/// <summary>
/// Graba lo que sale por el micrófono virtual (voz, sonidos, frases y música: tu karaoke tal y como lo oyen los demás)
/// en un WAV mono de 48 kHz. El hilo de audio solo copia cada bloque a un buffer sin bloqueos; un hilo aparte lo
/// escribe en disco. Al acabar, <see cref="ConvertToMp3"/> lo deja en MP3 para compartirlo.
/// </summary>
public sealed class MicRecorder : IDisposable
{
    // ~5,5 s de margen por si el disco se para un momento.
    private readonly SpscRingBuffer _buffer = new(1 << 18);
    private readonly Lock _lock = new();
    private volatile bool _recording;
    private WaveFileWriter? _writer;
    private Thread? _thread;
    private long _samplesWritten;
    private long _samplesDropped;

    public bool IsRecording => _recording;

    /// <summary>Lo grabado hasta ahora.</summary>
    public TimeSpan Elapsed => TimeSpan.FromSeconds((double)Interlocked.Read(ref _samplesWritten) / DspMath.SampleRate);

    /// <summary>Muestras perdidas porque el disco no daba abasto (debería ser 0).</summary>
    public long SamplesDropped => Interlocked.Read(ref _samplesDropped);

    /// <summary>Por qué se cortó la grabación (disco lleno, sin permiso...), o null.</summary>
    public string? Failure { get; private set; }

    /// <summary>Empieza a grabar en <paramref name="wavPath"/>. Desde el hilo de la UI.</summary>
    public void Start(string wavPath)
    {
        lock (_lock)
        {
            if (_recording) throw new InvalidOperationException("Ya se está grabando.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(wavPath))!);
            _writer = new WaveFileWriter(wavPath, new WaveFormat(DspMath.SampleRate, 16, 1));
            // Lo que quedara de la grabación anterior (el último bloque tras pararla) no es de esta.
            _buffer.Advance(_buffer.Available);
            Interlocked.Exchange(ref _samplesWritten, 0);
            Interlocked.Exchange(ref _samplesDropped, 0);
            Failure = null;
            _recording = true;
            _thread = new Thread(WriteLoop) { IsBackground = true, Name = "M0DV0IC3 grabación" };
            _thread.Start();
        }
    }

    /// <summary>Para, escribe lo que faltaba y cierra el WAV. Devuelve su duración.</summary>
    public TimeSpan Stop()
    {
        lock (_lock)
        {
            if (!_recording) return TimeSpan.Zero;
            _recording = false;
            _thread?.Join();
            _thread = null;
            _writer?.Dispose();
            _writer = null;
            return Elapsed;
        }
    }

    /// <summary>Hilo de audio: copia lo que sale por el micro (no reserva memoria ni espera).</summary>
    public void Write(ReadOnlySpan<float> block)
    {
        if (!_recording) return;
        int written = _buffer.Write(block);
        if (written < block.Length) Interlocked.Add(ref _samplesDropped, block.Length - written);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Pasa el WAV a MP3 de 128 kbps con el codificador de Windows y borra el WAV. Si este Windows no tiene
    /// codificador de MP3 (las ediciones N sin el paquete multimedia), deja el WAV. Devuelve el archivo que queda.
    /// </summary>
    public static string ConvertToMp3(string wavPath)
    {
        string mp3Path = Path.ChangeExtension(wavPath, ".mp3");
        try
        {
            MediaFoundationApi.Startup();
            using (var reader = new WaveFileReader(wavPath))
            {
                MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 128_000);
            }
            File.Delete(wavPath);
            return mp3Path;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try
            {
                File.Delete(mp3Path);
            }
            catch (IOException)
            {
                // Se queda a medias; lo que importa es el WAV.
            }
            return wavPath;
        }
    }

    private void WriteLoop()
    {
        var chunk = new float[8192];
        var writer = _writer!;
        try
        {
            while (true)
            {
                bool recording = _recording;
                int read;
                while ((read = _buffer.Read(chunk)) > 0)
                {
                    writer.WriteSamples(chunk, 0, read);
                    Interlocked.Add(ref _samplesWritten, read);
                }
                if (!recording) return;
                Thread.Sleep(30);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disco lleno o carpeta sin permiso: se para la grabación; lo escrito hasta aquí se conserva.
            Failure = ex.Message;
            _recording = false;
        }
    }
}
