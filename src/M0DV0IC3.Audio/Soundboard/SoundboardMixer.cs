using M0DV0IC3.Audio.Realtime;

namespace M0DV0IC3.Audio.Soundboard;

/// <summary>
/// Mezclador del soundboard con hasta <see cref="MaxVoices"/> sonidos a la vez. La UI manda órdenes
/// por una cola sin bloqueos y el hilo de audio las aplica al principio de cada bloque, sin reservar memoria.
/// Reproducir un sonido que ya suena lo reinicia. Si no hay voces libres, se sustituye la más antigua.
/// </summary>
public sealed class SoundboardMixer
{
    public const int MaxVoices = 8;

    private const int FadeOutSamples = 240;

    private readonly SpscQueue<Command> _commands = new(256);
    private readonly SoundClip?[] _clips = new SoundClip?[MaxVoices];
    private readonly int[] _positions = new int[MaxVoices];
    private readonly float[] _gains = new float[MaxVoices];
    private readonly int[] _fade = new int[MaxVoices];
    private readonly long[] _startOrder = new long[MaxVoices];
    private long _starts;
    private int _activeVoices;
    private float _masterVolume = 1f;

    private enum CommandKind
    {
        Play,
        Stop,
        StopAll,
    }

    private readonly record struct Command(CommandKind Kind, SoundClip? Clip, float Volume);

    /// <summary>Volumen general del soundboard (0..2).</summary>
    public float MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set => Volatile.Write(ref _masterVolume, Math.Clamp(value, 0f, 2f));
    }

    public int ActiveVoices => Volatile.Read(ref _activeVoices);

    public bool Play(SoundClip clip, float volume = 1f) => _commands.TryEnqueue(new Command(CommandKind.Play, clip, Math.Clamp(volume, 0f, 2f)));

    public bool Stop(SoundClip clip) => _commands.TryEnqueue(new Command(CommandKind.Stop, clip, 0f));

    public bool StopAll() => _commands.TryEnqueue(new Command(CommandKind.StopAll, null, 0f));

    /// <summary>Hilo de audio: escribe la mezcla en <paramref name="destination"/>. Devuelve false si no suena nada.</summary>
    public bool Render(Span<float> destination)
    {
        while (_commands.TryDequeue(out var command)) Apply(command);

        destination.Clear();
        float master = MasterVolume;
        int active = 0;
        bool rendered = false;

        for (int v = 0; v < MaxVoices; v++)
        {
            var clip = _clips[v];
            if (clip is null) continue;

            float[] samples = clip.Samples;
            int pos = _positions[v];
            float gain = _gains[v] * master;
            bool fading = _fade[v] > 0;
            int count = Math.Min(destination.Length, samples.Length - pos);
            if (fading) count = Math.Min(count, _fade[v]);

            if (fading)
            {
                float fade = _fade[v];
                for (int i = 0; i < count; i++)
                    destination[i] += samples[pos + i] * gain * ((fade - i) / FadeOutSamples);
                _fade[v] -= count;
            }
            else
            {
                for (int i = 0; i < count; i++)
                    destination[i] += samples[pos + i] * gain;
            }

            rendered |= count > 0;
            pos += count;
            if (pos >= samples.Length || (fading && _fade[v] <= 0))
            {
                _clips[v] = null;
                _fade[v] = 0;
                continue;
            }
            _positions[v] = pos;
            active++;
        }

        Volatile.Write(ref _activeVoices, active);
        return rendered;
    }

    private void Apply(Command command)
    {
        switch (command.Kind)
        {
            case CommandKind.Play when command.Clip is { Samples.Length: > 0 }:
            {
                int voice = Array.IndexOf(_clips, command.Clip);
                if (voice < 0) voice = Array.IndexOf(_clips, null);
                if (voice < 0)
                {
                    voice = 0;
                    for (int v = 1; v < MaxVoices; v++)
                        if (_startOrder[v] < _startOrder[voice]) voice = v;
                }
                _clips[voice] = command.Clip;
                _positions[voice] = 0;
                _gains[voice] = command.Volume;
                _fade[voice] = 0;
                _startOrder[voice] = ++_starts;
                break;
            }
            case CommandKind.Stop:
            {
                for (int v = 0; v < MaxVoices; v++)
                    if (ReferenceEquals(_clips[v], command.Clip) && _fade[v] == 0) _fade[v] = FadeOutSamples;
                break;
            }
            case CommandKind.StopAll:
            {
                for (int v = 0; v < MaxVoices; v++)
                    if (_clips[v] is not null && _fade[v] == 0) _fade[v] = FadeOutSamples;
                break;
            }
        }
    }
}
