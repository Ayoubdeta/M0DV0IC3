using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.Audio.Speech;

/// <summary>
/// Texto a voz por el micro: reproduce una frase ya sintetizada y la pasa por su propia cadena de voz. Es la misma voz
/// que tengas puesta, pero con su propio tono medio (el de la voz de Windows, no el tuyo): así las voces con tono
/// objetivo (Mujer, Grave...) cambian lo justo, y la frase no estropea lo aprendido de tu voz.
/// <para>La UI manda las órdenes por una cola sin bloqueos y el hilo de audio no reserva memoria. Una frase nueva corta
/// la que estuviera sonando.</para>
/// </summary>
public sealed class SpeechPlayer
{
    // Tras la frase se procesa un poco de silencio, para que el eco y la reverb de la voz se apaguen solos.
    private const int TailSamples = DspMath.SampleRate * 2;
    private const int FadeOutSamples = 240;

    private readonly SpscQueue<Command> _commands = new(16);
    private SoundClip? _clip;
    private int _position;
    private int _tail;
    private int _fade;
    private float _volume = 1f;
    private volatile bool _isPlaying;

    private readonly record struct Command(SoundClip? Clip, double PitchHz);

    public SpeechPlayer(int maxBlockSize = VoicePipeline.MaxBlockSize)
    {
        Voice = new VoiceProcessor(DspMath.SampleRate, maxBlockSize);
    }

    /// <summary>La voz que se aplica a las frases (la app le manda la misma que a tu micro, o la normal).</summary>
    public VoiceProcessor Voice { get; }

    /// <summary>Volumen de las frases (0..2).</summary>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>Suena una frase (o se apaga su eco).</summary>
    public bool IsPlaying => _isPlaying;

    /// <param name="pitchHz">Tono medio de la frase (<see cref="EstimatePitch"/>), o 0 si no se sabe.</param>
    public bool Play(SoundClip clip, double pitchHz = 0) => _commands.TryEnqueue(new Command(clip, pitchHz));

    public bool Stop() => _commands.TryEnqueue(new Command(null, 0));

    /// <summary>Hilo de audio: escribe la frase con la voz en <paramref name="destination"/>. Devuelve false si no suena nada.</summary>
    public bool Render(Span<float> destination)
    {
        while (_commands.TryDequeue(out var command)) Apply(command);

        if (_clip is null && _tail <= 0 && _fade <= 0)
        {
            _isPlaying = false;
            return false;
        }

        destination.Clear();
        if (_clip is { } clip)
        {
            int count = Math.Min(destination.Length, clip.Samples.Length - _position);
            clip.Samples.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            if (_position >= clip.Samples.Length)
            {
                _clip = null;
                _tail = TailSamples;
            }
        }
        else if (_fade <= 0)
        {
            _tail -= destination.Length;
        }

        Voice.Process(destination);

        float volume = Volume;
        if (_fade > 0)
        {
            int count = Math.Min(destination.Length, _fade);
            for (int i = 0; i < count; i++) destination[i] *= volume * (_fade - i) / FadeOutSamples;
            destination[count..].Clear();
            _fade -= count;
            if (_fade <= 0) Voice.Reset();
        }
        else if (volume != 1f)
        {
            for (int i = 0; i < destination.Length; i++) destination[i] *= volume;
        }

        _isPlaying = true;
        return true;
    }

    /// <summary>Tono medio de la parte sonora de una frase, en Hz (0 si casi no tiene voz). Fuera del hilo de audio.</summary>
    public static double EstimatePitch(ReadOnlySpan<float> samples, int sampleRate = DspMath.SampleRate)
    {
        const int Hop = 128;
        var detector = new YinPitchDetector(sampleRate, 60, 500, Hop);
        double logSum = 0;
        int voiced = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            detector.Push(samples[i]);
            if (i % Hop == Hop - 1 && detector.IsVoiced && detector.PeriodSamples > 0)
            {
                logSum += Math.Log(sampleRate / detector.PeriodSamples);
                voiced++;
            }
        }
        // Menos de ~0,1 s de voz no da para fiarse.
        return voiced * Hop < sampleRate / 10 ? 0 : Math.Exp(logSum / voiced);
    }

    private void Apply(Command command)
    {
        if (command.Clip is { Samples.Length: > 0 } clip)
        {
            _clip = clip;
            _position = 0;
            _tail = 0;
            _fade = 0;
            if (command.PitchHz > 0) Voice.Profile.Seed(command.PitchHz);
        }
        else if (command.Clip is null && (_clip is not null || _tail > 0))
        {
            _clip = null;
            _tail = 0;
            _fade = FadeOutSamples;
        }
    }
}
