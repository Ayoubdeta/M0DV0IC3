using M0DV0IC3.Dsp.Effects;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.Cli.Commands;

internal static class VoicesCommand
{
    public static int Run(string[] args)
    {
        CliArgs.Parse(args, [], []).ExpectPositional(0);

        var table = new TextTable("Id", "Nombre", "Tono (st)", "Formantes", "PSOLA", "Efectos").AlignRight(2, 3);
        foreach (var voice in AllVoices())
        {
            table.Add(
                voice.Id,
                voice.Name,
                voice.RobotHz > 0 ? "robot" : voice.AutotuneScale != AutotuneScale.Off ? "autotune" : $"{voice.PitchSemitones:+0.#;-0.#;0}",
                $"{voice.FormantRatio:0.00}",
                voice.UsesPitch ? "sí" : "no",
                DescribeEffects(voice));
        }
        table.Print();
        Console.WriteLine();
        Console.WriteLine("Solo añaden latencia PSOLA (unos 2 periodos de la voz), la voz invertida (un trozo entero) y el vibrato sin PSOLA (su retardo medio).");
        return 0;
    }

    public static IEnumerable<VoicePreset> AllVoices() => BuiltInVoices.All.Prepend(VoicePreset.Neutral);

    public static string DescribeEffects(VoicePreset p)
    {
        var parts = new List<string>();
        if (p.TargetPitchHz > 0) parts.Add($"tono objetivo {p.TargetPitchHz:0} Hz");
        if (p.RobotHz > 0) parts.Add($"monótono {p.RobotHz:0} Hz");
        if (p.AutotuneScale != AutotuneScale.Off)
        {
            string tonality = p.PitchSemitones != 0 ? $"{p.PitchSemitones:+0.#;-0.#} st, " : "";
            string exaggeration = p.AutotuneExaggeration > 0 ? $", melodía ×{1 + 1.5 * p.AutotuneExaggeration:0.0#}" : "";
            parts.Add($"autotune {tonality}{AutotuneNames.Describe(p.AutotuneScale, p.AutotuneKey)}, {(p.AutotuneRetuneMs > 0 ? $"{p.AutotuneRetuneMs:0} ms" : "instantáneo")}{exaggeration}");
        }
        if (p.VibratoSemitones > 0) parts.Add($"vibrato {p.VibratoHz:0.##} Hz ±{p.VibratoSemitones:0.#} st");
        if (p.ReverseMs > 0) parts.Add($"invertida {p.ReverseMs:0} ms");
        if (p.HarmonyMix > 0) parts.Add($"coro {p.HarmonyMix:P0}");
        if (p.WhisperMix > 0) parts.Add($"susurro {p.WhisperMix:P0}");
        if (p.Breathiness > 0) parts.Add($"aire {p.Breathiness:P0}");
        if (p.VocoderMix > 0) parts.Add($"vocoder {p.VocoderMix:P0} ({p.VocoderHz:0} Hz)");
        if (p.RingModHz > 0 && p.RingModMix > 0) parts.Add($"ring {p.RingModHz:0} Hz {p.RingModMix:P0}");
        if (p.CombMs > 0 && p.CombMix > 0) parts.Add($"comb {p.CombMs:0.#} ms fb {p.CombFeedback:0.00}");
        if (p.Distortion > 0) parts.Add($"distorsión {p.Distortion:0.00}");
        if (p.BitcrushRateHz > 0 || p.BitcrushBits > 0)
            parts.Add($"bitcrush {(p.BitcrushRateHz > 0 ? $"{p.BitcrushRateHz / 1000:0.#} kHz" : "")}{(p.BitcrushBits > 0 ? $" {p.BitcrushBits} bits" : "")}".TrimEnd());
        if (p.HighPassHz > 0) parts.Add($"HP {p.HighPassHz:0} Hz");
        if (p.LowPassHz > 0) parts.Add($"LP {p.LowPassHz:0} Hz");
        if (Math.Abs(p.PresenceDb) > 0.05) parts.Add($"presencia {p.PresenceDb:+0.#;-0.#} dB");
        if (Math.Abs(p.HighShelfDb) > 0.05) parts.Add($"agudos {p.HighShelfDb:+0.#;-0.#} dB");
        if (Math.Abs(p.LowShelfDb) > 0.05) parts.Add($"graves {p.LowShelfDb:+0.#;-0.#} dB");
        if (Math.Abs(p.NasalDb) > 0.05) parts.Add($"nasal {p.NasalDb:+0.#;-0.#} dB");
        if (p.Compression > 0) parts.Add($"compresión {p.Compression:P0}");
        if (p.Transmission != TransmissionStyle.Off)
            parts.Add($"{(p.Transmission == TransmissionStyle.Walkie ? "walkie" : "pitidos NASA")} {p.TransmissionNoise:P0}");
        if (p.ChorusMix > 0) parts.Add($"chorus {p.ChorusMix:P0}");
        if (p.FlangerMix > 0) parts.Add($"flanger {p.FlangerMix:P0} a {p.FlangerHz:0.##} Hz");
        if (p.EchoMs > 0 && p.EchoMix > 0) parts.Add($"eco {p.EchoMs:0} ms {p.EchoMix:P0}");
        if (p.ReverbMix > 0) parts.Add($"reverb {p.ReverbMix:P0} (tamaño {p.ReverbSize:0.00})");
        if (Math.Abs(p.OutputGainDb) > 0.05) parts.Add($"ganancia {p.OutputGainDb:+0.#;-0.#} dB");
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    /// <summary>Busca la voz por id; si no existe, el error lista las válidas.</summary>
    public static VoicePreset Find(string id) =>
        BuiltInVoices.Find(id.ToLowerInvariant())
        ?? throw new CliException($"Voz desconocida: '{id}'. Voces válidas: {string.Join(", ", AllVoices().Select(v => v.Id))}");
}
