using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace M0DV0IC3.Audio.Wasapi;

internal sealed record InitializedClient(AudioClient Client, WaveFormat Format, SampleLayout Layout, StreamMode Mode, int PeriodFrames);

/// <summary>
/// Abre un <see cref="AudioClient"/> con la menor latencia disponible, siempre a 48 kHz:
/// <list type="number">
/// <item>Exclusivo, si se pide: periodo mínimo del hardware (~3 ms).</item>
/// <item>Compartido de baja latencia (IAudioClient3), si el driver lo admite y el mezclador de Windows ya va a 48 kHz.</item>
/// <item>Compartido normal (~10 ms) con AUTOCONVERTPCM: Windows remuestrea al vuelo si el dispositivo no va a 48 kHz.</item>
/// </list>
/// </summary>
internal static class WasapiInitializer
{
    public const int SampleRate = 48000;

    private const int AudclntEBufferSizeNotAligned = unchecked((int)0x88890019);
    private const long ReferenceTimePerSecond = 10_000_000;

    public static InitializedClient Open(MMDevice device, bool exclusive, bool preferLowLatency, bool isCapture)
    {
        if (exclusive)
        {
            try
            {
                return OpenExclusive(device);
            }
            catch (Exception ex) when (ex is COMException or NotSupportedException)
            {
                // Formato no admitido u otra app tiene el dispositivo en exclusivo: mejor compartido que nada.
                // StreamInfo.Mode lo refleja para que la UI lo muestre.
            }
        }

        var client = device.CreateAudioClient();
        try
        {
            var mix = client.MixFormat;
            if (preferLowLatency && TryOpenLowLatency(ref client, device, mix, out var lowLatency)) return lowLatency!;

            // El buffer de captura se lee paquete a paquete y el de salida se rellena solo hasta un nivel
            // objetivo, así que un buffer algo mayor no añade latencia: solo deja margen.
            var format = FloatFormat(mix.Channels, mix);
            long bufferDuration = (isCapture ? 20 : 40) * ReferenceTimePerSecond / 1000;
            client.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                bufferDuration, 0, format, Guid.Empty);
            int period = (int)Math.Round(client.DefaultDevicePeriod * (double)SampleRate / ReferenceTimePerSecond);
            return new InitializedClient(client, format, SampleLayout.From(format), StreamMode.Shared, period);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static bool TryOpenLowLatency(ref AudioClient client, MMDevice device, WaveFormat mix, out InitializedClient? result)
    {
        result = null;
        if (!client.SupportsAudioClient3 || mix.SampleRate != SampleRate || !SampleLayout.TryFrom(mix, out var layout)) return false;
        try
        {
            var periods = client.GetSharedModeEnginePeriod(mix);
            uint period = periods.ChooseLowestLatencyPeriod();
            if (period == 0 || period >= periods.DefaultPeriodInFrames) return false;
            client.InitializeSharedAudioStream(AudioClientStreamFlags.EventCallback, period, mix, Guid.Empty);
            result = new InitializedClient(client, mix, layout, StreamMode.SharedLowLatency, (int)period);
            return true;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or InvalidOperationException)
        {
            // Si falla, el cliente ya no sirve: se crea otro para el modo normal.
            client.Dispose();
            client = device.CreateAudioClient();
            return false;
        }
    }

    private static InitializedClient OpenExclusive(MMDevice device)
    {
        var client = device.CreateAudioClient();
        try
        {
            var mix = client.MixFormat;
            WaveFormat? chosen = null;
            foreach (int channels in new[] { mix.Channels, 2, 1 }.Distinct())
            {
                foreach (var candidate in ExclusiveCandidates(channels, mix))
                {
                    if (client.IsFormatSupported(AudioClientShareMode.Exclusive, candidate))
                    {
                        chosen = candidate;
                        break;
                    }
                }
                if (chosen is not null) break;
            }
            if (chosen is null) throw new NotSupportedException("El dispositivo no admite 48 kHz en modo exclusivo.");

            long period = client.MinimumDevicePeriod;
            try
            {
                client.Initialize(AudioClientShareMode.Exclusive, AudioClientStreamFlags.EventCallback, period, period, chosen, Guid.Empty);
            }
            catch (COMException ex) when (ex.HResult == AudclntEBufferSizeNotAligned)
            {
                // El periodo pedido no cuadra con el tamaño de bloque del hardware: se usa el alineado que propone.
                int alignedFrames = client.BufferSize;
                client.Dispose();
                period = (long)Math.Round(ReferenceTimePerSecond * (double)alignedFrames / SampleRate);
                client = device.CreateAudioClient();
                client.Initialize(AudioClientShareMode.Exclusive, AudioClientStreamFlags.EventCallback, period, period, chosen, Guid.Empty);
            }
            return new InitializedClient(client, chosen, SampleLayout.From(chosen), StreamMode.Exclusive, client.BufferSize);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static IEnumerable<WaveFormat> ExclusiveCandidates(int channels, WaveFormat mix)
    {
        int mask = ChannelMask(channels, mix);
        yield return new WaveFormatExtensible(SampleRate, 32, channels, true, 32, mask);
        yield return new WaveFormatExtensible(SampleRate, 32, channels, false, 24, mask);
        yield return new WaveFormatExtensible(SampleRate, 24, channels, false, 24, mask);
        yield return new WaveFormatExtensible(SampleRate, 16, channels, false, 16, mask);
    }

    private static WaveFormatExtensible FloatFormat(int channels, WaveFormat mix) =>
        new(SampleRate, 32, channels, true, 32, ChannelMask(channels, mix));

    private static int ChannelMask(int channels, WaveFormat mix)
    {
        if (mix is WaveFormatExtensible ext && ext.Channels == channels && ext.ChannelMask != 0) return ext.ChannelMask;
        return channels switch
        {
            1 => 0x4,
            2 => 0x3,
            4 => 0x33,
            6 => 0x3F,
            8 => 0x63F,
            _ => (1 << channels) - 1,
        };
    }
}
