using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Dsp.Presets;

/// <summary>
/// Gestiona la voz activa y los cambios de voz sin clics: la cadena nueva se construye en el hilo
/// que la pide, se entrega al hilo de audio con un intercambio atómico y se mezcla con la anterior
/// en un crossfade de igual potencia de 30 ms.
/// </summary>
public sealed class VoiceProcessor : IAudioEffect
{
    private readonly int _sampleRate;
    private readonly int _fadeLength;
    private readonly float[] _scratch;
    private VoiceChain _current;
    private VoiceChain? _fadingOut;
    private VoiceChain? _incoming;
    private VoiceChain _latestRequested;
    private int _fadePos;

    public VoiceProcessor(int sampleRate = DspMath.SampleRate, int maxBlockSize = 8192)
    {
        _sampleRate = sampleRate;
        _fadeLength = sampleRate * 30 / 1000;
        _scratch = new float[maxBlockSize];
        _current = _latestRequested = new VoiceChain(VoicePreset.Neutral, sampleRate, Range);
    }

    /// <summary>Rango de voz con el que se crean las cadenas con PSOLA. Al cambiarlo, vuelve a llamar a <see cref="SetPreset"/>.</summary>
    public VoiceRange Range { get; set; } = VoiceRange.Medium;

    /// <summary>Preset activo o pendiente de activar (la UI lo usa como fuente de verdad).</summary>
    public VoicePreset CurrentPreset => _latestRequested.Preset;

    public int LatencySamples => Volatile.Read(ref _current).LatencySamples;

    /// <summary>
    /// Cambia de voz. Con <paramref name="liveEdit"/> (los sliders del editor) intenta actualizar la cadena
    /// actual en caliente, sin crossfade ni reiniciar estados. No es seguro llamarlo desde varios hilos a la vez.
    /// </summary>
    public void SetPreset(VoicePreset preset, bool liveEdit = false)
    {
        var latest = _latestRequested;
        if (liveEdit && latest.Range == Range && latest.TryUpdate(preset)) return;

        var chain = new VoiceChain(preset, _sampleRate, Range);
        _latestRequested = chain;
        Volatile.Write(ref _incoming, chain);
    }

    public void Process(Span<float> buffer)
    {
        if (buffer.Length > _scratch.Length) throw new ArgumentException("Bloque demasiado grande", nameof(buffer));

        var incoming = Interlocked.Exchange(ref _incoming, null);
        if (incoming is not null)
        {
            // Si llega una voz nueva en mitad de un crossfade, la más antigua se descarta.
            _fadingOut = _current;
            Volatile.Write(ref _current, incoming);
            _fadePos = 0;
        }

        if (_fadingOut is null)
        {
            _current.Process(buffer);
            return;
        }

        var old = _scratch.AsSpan(0, buffer.Length);
        buffer.CopyTo(old);
        _fadingOut.Process(old);
        _current.Process(buffer);

        float inverseLength = 1f / _fadeLength;
        for (int i = 0; i < buffer.Length; i++)
        {
            float t = Math.Min(1f, (_fadePos + i) * inverseLength);
            float angle = t * (MathF.PI / 2);
            buffer[i] = buffer[i] * MathF.Sin(angle) + old[i] * MathF.Cos(angle);
        }

        _fadePos += buffer.Length;
        if (_fadePos >= _fadeLength) _fadingOut = null;
    }

    public void Reset()
    {
        _current.Reset();
        _fadingOut = null;
    }
}
