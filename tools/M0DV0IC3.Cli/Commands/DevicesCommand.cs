using M0DV0IC3.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace M0DV0IC3.Cli.Commands;

/// <summary>Lista los endpoints y lo que admiten. Solo consulta: nunca inicializa ni arranca un cliente.</summary>
internal static class DevicesCommand
{
    private const double ReferenceTimePerMs = 10_000;

    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");

    public static int Run(string[] args)
    {
        CliArgs.Parse(args, [], []).ExpectPositional(0);

        using var enumerator = new MMDeviceEnumerator();
        bool hasCableInput = false;
        foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
        {
            Console.WriteLine(flow == DataFlow.Capture ? "CAPTURA (micrófonos)" : "SALIDA (altavoces, auriculares, cables virtuales)");
            string? consoleDefault = DefaultId(enumerator, flow, Role.Console);
            string? communicationsDefault = DefaultId(enumerator, flow, Role.Communications);

            using var collection = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            if (collection.Count == 0) Console.WriteLine("  (ninguno activo)");
            foreach (var device in collection)
            {
                using (device)
                {
                    string name = device.FriendlyName;
                    bool isCable = DeviceService.IsVirtualCableName(name);
                    if (flow == DataFlow.Render && isCable && name.Contains("Input", StringComparison.OrdinalIgnoreCase)) hasCableInput = true;

                    var marks = new List<string>();
                    if (device.ID == consoleDefault) marks.Add("[predeterminado]");
                    if (device.ID == communicationsDefault) marks.Add("[comunicaciones]");
                    if (isCable) marks.Add("[VB-Cable]");
                    Console.WriteLine($"  * {name} {string.Join(' ', marks)}".TrimEnd());
                    Console.WriteLine($"    ID: {device.ID}");
                    Describe(device);
                }
            }
            Console.WriteLine();
        }

        if (!hasCableInput)
        {
            Console.WriteLine("No está instalado VB-Audio Virtual Cable (falta la salida \"CABLE Input\").");
            Console.WriteLine($"Sin él, Discord y los juegos no pueden recibir la voz modificada. Descárgalo de {DeviceService.CableDownloadUrl}");
        }
        return 0;
    }

    private static void Describe(MMDevice device)
    {
        try
        {
            using var client = device.CreateAudioClient();
            var mix = client.MixFormat;
            Console.WriteLine($"    Formato de mezcla: {DescribeFormat(mix)}");
            Console.WriteLine($"    Periodo del dispositivo: predeterminado {client.DefaultDevicePeriod / ReferenceTimePerMs:0.00} ms" +
                $" · mínimo {client.MinimumDevicePeriod / ReferenceTimePerMs:0.00} ms");

            if (client.SupportsAudioClient3)
            {
                var periods = client.GetSharedModeEnginePeriod(mix);
                double perMs = mix.SampleRate / 1000.0;
                Console.WriteLine($"    Motor compartido (IAudioClient3): predeterminado {periods.DefaultPeriodInFrames} ({periods.DefaultPeriodInFrames / perMs:0.00} ms)" +
                    $" · mínimo {periods.MinPeriodInFrames} ({periods.MinPeriodInFrames / perMs:0.00} ms)" +
                    $" · paso {periods.FundamentalPeriodInFrames} · máximo {periods.MaxPeriodInFrames}");
            }
            else
            {
                Console.WriteLine("    Motor compartido: sin IAudioClient3 (sin modo de baja latencia)");
            }

            if (mix.SampleRate != 48000)
                Console.WriteLine($"    Nota: el mezclador va a {mix.SampleRate} Hz; la app usará el modo compartido normal con conversión a 48 kHz.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    (no se pudo consultar: {ex.Message})");
        }
    }

    private static string DescribeFormat(WaveFormat format)
    {
        string kind = format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat => "float",
            WaveFormatEncoding.Pcm => "PCM",
            WaveFormatEncoding.Extensible when format is WaveFormatExtensible ext =>
                ext.SubFormat == FloatSubFormat ? "float" : ext.SubFormat == PcmSubFormat ? "PCM" : ext.SubFormat.ToString(),
            _ => format.Encoding.ToString(),
        };
        return $"{format.SampleRate} Hz · {format.BitsPerSample} bits {kind} · {format.Channels} {(format.Channels == 1 ? "canal" : "canales")}";
    }

    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        if (!enumerator.TryGetDefaultAudioEndpoint(flow, role, out var device)) return null;
        using (device) return device.ID;
    }
}
