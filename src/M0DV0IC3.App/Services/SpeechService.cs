using System.IO;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Audio.Speech;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace M0DV0IC3.App.Services;

/// <summary>Una voz de Windows para el texto a voz ("Helena", "es-ES").</summary>
public sealed record SpeechVoiceInfo(string Id, string Name, string Language, string Gender)
{
    public string Display => $"{Name} ({Language}, {Gender})";
}

/// <summary>Una frase ya sintetizada y lista para sonar, con su tono medio.</summary>
public sealed record SpokenPhrase(SoundClip Clip, double PitchHz);

/// <summary>
/// Texto a voz con las voces de Windows (las del Narrador: en un Windows en español, Helena, Laura y Pablo), sin
/// internet. Medido en un i7-7700: una frase de 5 s tarda 60-160 ms en sintetizarse.
/// </summary>
public static class SpeechService
{
    public const int MaxLength = 300;

    /// <summary>Las voces instaladas, primero las de español.</summary>
    public static IReadOnlyList<SpeechVoiceInfo> GetVoices()
    {
        try
        {
            return SpeechSynthesizer.AllVoices
                .Select(v => new SpeechVoiceInfo(v.Id, v.DisplayName.Replace("Microsoft ", ""), v.Language,
                    v.Gender == VoiceGender.Female ? "mujer" : "hombre"))
                .OrderBy(v => v.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(v => v.Name, StringComparer.CurrentCulture)
                .ToList();
        }
        catch (Exception ex)
        {
            // Windows sin el componente de voz (algunas ediciones recortadas).
            Log.Warn("No se pudieron leer las voces de Windows", ex);
            return [];
        }
    }

    /// <summary>Sintetiza la frase a 48 kHz mono, sin los silencios del principio y del final.</summary>
    public static async Task<SpokenPhrase> SynthesizeAsync(string text, string? voiceId, double rate)
    {
        var memory = new MemoryStream();
        using (var synthesizer = new SpeechSynthesizer())
        {
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId);
            if (voice is not null) synthesizer.Voice = voice;
            synthesizer.Options.SpeakingRate = Math.Clamp(rate, 0.5, 2);
            using var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
            using var input = stream.AsStreamForRead();
            await input.CopyToAsync(memory);
        }

        return await Task.Run(() =>
        {
            memory.Position = 0;
            using var reader = new WaveFileReader(memory);
            var clip = SoundLoader.Decode(reader, "frase", text);
            var (start, end) = SoundTrim.DetectSound(clip.Samples);
            if (end > start) clip = SoundTrim.Apply(clip, start, end);
            return new SpokenPhrase(clip, SpeechPlayer.EstimatePitch(clip.Samples));
        });
    }
}
