namespace M0DV0IC3.Audio.Wasapi;

public enum StreamMode
{
    /// <summary>Modo compartido normal (periodo de ~10 ms); Windows convierte el formato si hace falta.</summary>
    Shared,

    /// <summary>Modo compartido de baja latencia (IAudioClient3): periodos de ~2,7-5 ms si el driver lo admite.</summary>
    SharedLowLatency,

    /// <summary>Modo exclusivo: el dispositivo es solo para esta app; periodo mínimo del hardware.</summary>
    Exclusive,
}

/// <summary>Cómo quedó abierto un stream WASAPI.</summary>
public sealed record StreamInfo(
    string DeviceName,
    StreamMode Mode,
    int SampleRate,
    string Format,
    int PeriodFrames,
    int BufferFrames)
{
    public double PeriodMs => PeriodFrames * 1000.0 / SampleRate;

    public string ModeDescription => Mode switch
    {
        StreamMode.SharedLowLatency => "compartido baja latencia",
        StreamMode.Exclusive => "exclusivo",
        _ => "compartido",
    };
}
