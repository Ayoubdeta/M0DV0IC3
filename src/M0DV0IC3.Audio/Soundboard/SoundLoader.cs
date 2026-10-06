using M0DV0IC3.Dsp;
using NAudio.Wave;
using NAudio.Vorbis;
using NAudio.Wave.SampleProviders;

namespace M0DV0IC3.Audio.Soundboard;

/// <summary>Decodifica archivos de audio (WAV, MP3, AIFF y lo que soporte Media Foundation) a mono float 48 kHz.</summary>
public static class SoundLoader
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<string> SupportedExtensions { get; } = [".wav", ".mp3", ".aiff", ".aif", ".wma", ".m4a", ".aac", ".flac", ".ogg"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Carga el archivo entero. Es lento (cientos de ms): llámalo fuera del hilo de la UI.</summary>
    public static SoundClip Load(string path, string id, string name)
    {
        using var reader = Open(path);
        return Decode(reader, id, name);
    }

    /// <summary>Decodifica audio ya abierto (p. ej., una frase de texto a voz en memoria). Fuera del hilo de la UI.</summary>
    public static SoundClip Decode(WaveStream reader, string id, string name)
    {
        ISampleProvider provider = reader.ToSampleProvider();
        if (provider.WaveFormat.SampleRate != DspMath.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, DspMath.SampleRate);

        int channels = provider.WaveFormat.Channels;
        int maxFrames = (int)(MaxDuration.TotalSeconds * DspMath.SampleRate);
        var buffer = new float[4096 * channels];
        var samples = new List<float>(DspMath.SampleRate * 4);

        while (samples.Count < maxFrames)
        {
            int read = provider.Read(buffer);
            if (read <= 0) break;
            for (int i = 0; i + channels <= read; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += buffer[i + c];
                samples.Add(sum / channels);
            }
        }

        return new SoundClip(id, name, samples.ToArray());
    }

    /// <summary>
    /// WAV y AIFF con los lectores de NAudio, OGG Vorbis con NVorbis (Windows no lo decodifica) y MP3, WMA, AAC,
    /// FLAC… con Media Foundation (el decodificador de Windows).
    /// </summary>
    private static WaveStream Open(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".aiff" or ".aif") return new AiffFileReader(path);
        if (extension == ".ogg")
        {
            try
            {
                return new VorbisWaveReader(path);
            }
            catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
            {
                // Un OGG que no es Vorbis (Opus, FLAC…): que lo intente Media Foundation.
            }
        }
        if (extension == ".wav")
        {
            var wav = new WaveFileReader(path);
            if (wav.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible) return wav;
            // WAV comprimido (ADPCM, µ-law…): que lo decodifique Windows.
            wav.Dispose();
        }
        return new MediaFoundationReader(path);
    }
}
