using NAudio.Wave;

namespace M0DV0IC3.Audio.Wasapi;

internal enum SampleEncoding
{
    Float32,
    Pcm16,
    Pcm24,
    Pcm32,
}

/// <summary>Descripción mínima de un formato PCM/float intercalado que sabemos convertir.</summary>
internal readonly record struct SampleLayout(SampleEncoding Encoding, int Channels, int BytesPerSample)
{
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");

    public int BlockAlign => Channels * BytesPerSample;

    public static bool TryFrom(WaveFormat format, out SampleLayout layout)
    {
        layout = default;
        bool isFloat;
        switch (format.Encoding)
        {
            case WaveFormatEncoding.IeeeFloat:
                isFloat = true;
                break;
            case WaveFormatEncoding.Pcm:
                isFloat = false;
                break;
            case WaveFormatEncoding.Extensible when format is WaveFormatExtensible ext:
                if (ext.SubFormat == FloatSubFormat) isFloat = true;
                else if (ext.SubFormat == PcmSubFormat) isFloat = false;
                else return false;
                break;
            default:
                return false;
        }

        SampleEncoding? encoding = (isFloat, format.BitsPerSample) switch
        {
            (true, 32) => SampleEncoding.Float32,
            (false, 16) => SampleEncoding.Pcm16,
            (false, 24) => SampleEncoding.Pcm24,
            (false, 32) => SampleEncoding.Pcm32,
            _ => null,
        };
        if (encoding is null || format.Channels <= 0) return false;
        layout = new SampleLayout(encoding.Value, format.Channels, format.BitsPerSample / 8);
        return layout.BlockAlign == format.BlockAlign;
    }

    public static SampleLayout From(WaveFormat format) =>
        TryFrom(format, out var layout) ? layout : throw new NotSupportedException($"Formato de audio no soportado: {format}");

    public override string ToString()
    {
        string channels = Channels == 1 ? "1 canal" : $"{Channels} canales";
        return Encoding switch
        {
            SampleEncoding.Float32 => $"float 32 bits, {channels}",
            SampleEncoding.Pcm16 => $"PCM 16 bits, {channels}",
            SampleEncoding.Pcm24 => $"PCM 24 bits, {channels}",
            _ => $"PCM 32 bits, {channels}",
        };
    }
}

/// <summary>Conversión entre el formato intercalado del dispositivo y nuestro mono float, sin reservar memoria.</summary>
internal static unsafe class SampleConverter
{
    public static void ToMono(IntPtr source, int frames, in SampleLayout layout, Span<float> destination)
    {
        int channels = layout.Channels;
        float scale = 1f / channels;
        byte* p = (byte*)source;

        switch (layout.Encoding)
        {
            case SampleEncoding.Float32:
            {
                float* s = (float*)p;
                if (channels == 1)
                {
                    new ReadOnlySpan<float>(s, frames).CopyTo(destination);
                    return;
                }
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++) sum += *s++;
                    destination[f] = sum * scale;
                }
                return;
            }
            case SampleEncoding.Pcm16:
            {
                short* s = (short*)p;
                for (int f = 0; f < frames; f++)
                {
                    int sum = 0;
                    for (int c = 0; c < channels; c++) sum += *s++;
                    destination[f] = sum * scale / 32768f;
                }
                return;
            }
            case SampleEncoding.Pcm24:
            {
                for (int f = 0; f < frames; f++)
                {
                    int sum = 0;
                    for (int c = 0; c < channels; c++, p += 3) sum += (p[0] << 8 | p[1] << 16 | p[2] << 24) >> 8;
                    destination[f] = sum * scale / 8388608f;
                }
                return;
            }
            case SampleEncoding.Pcm32:
            {
                int* s = (int*)p;
                for (int f = 0; f < frames; f++)
                {
                    double sum = 0;
                    for (int c = 0; c < channels; c++) sum += *s++;
                    destination[f] = (float)(sum * scale / 2147483648.0);
                }
                return;
            }
        }
    }

    public static void FromMono(ReadOnlySpan<float> source, IntPtr destination, int frames, in SampleLayout layout)
    {
        int channels = layout.Channels;
        byte* p = (byte*)destination;

        switch (layout.Encoding)
        {
            case SampleEncoding.Float32:
            {
                float* d = (float*)p;
                for (int f = 0; f < frames; f++)
                {
                    float v = source[f];
                    for (int c = 0; c < channels; c++) *d++ = v;
                }
                return;
            }
            case SampleEncoding.Pcm16:
            {
                short* d = (short*)p;
                for (int f = 0; f < frames; f++)
                {
                    short v = (short)Math.Clamp(MathF.Round(source[f] * 32767f), short.MinValue, short.MaxValue);
                    for (int c = 0; c < channels; c++) *d++ = v;
                }
                return;
            }
            case SampleEncoding.Pcm24:
            {
                for (int f = 0; f < frames; f++)
                {
                    int v = (int)Math.Clamp(MathF.Round(source[f] * 8388607f), -8388608f, 8388607f);
                    for (int c = 0; c < channels; c++)
                    {
                        *p++ = (byte)v;
                        *p++ = (byte)(v >> 8);
                        *p++ = (byte)(v >> 16);
                    }
                }
                return;
            }
            case SampleEncoding.Pcm32:
            {
                int* d = (int*)p;
                for (int f = 0; f < frames; f++)
                {
                    int v = (int)Math.Clamp(Math.Round(source[f] * 2147483647.0), int.MinValue, int.MaxValue);
                    for (int c = 0; c < channels; c++) *d++ = v;
                }
                return;
            }
        }
    }
}
