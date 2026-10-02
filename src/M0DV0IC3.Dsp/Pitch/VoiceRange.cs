namespace M0DV0IC3.Dsp.Pitch;

/// <summary>
/// Rango de tono del usuario, para que el detector siga bien su voz. La latencia de PSOLA no depende
/// de este ajuste sino del tono real (~2,4 periodos): ~11 ms con voz aguda, ~20 ms con voz media
/// y ~27 ms con voz muy grave. Un rango demasiado alto para la voz provoca errores de octava.
/// </summary>
public enum VoiceRange
{
    /// <summary>Voces extremadamente graves, 50-700 Hz.</summary>
    Low,

    /// <summary>Casi todas las voces, 65-900 Hz (por defecto).</summary>
    Medium,

    /// <summary>Solo voces agudas (mujer, niño), 120-1000 Hz. Con una voz más grave no cambiaría el tono.</summary>
    High,
}

public static class VoiceRangeExtensions
{
    public static double MinFrequency(this VoiceRange range) => range switch
    {
        VoiceRange.Low => 50.0,
        VoiceRange.High => 120.0,
        _ => 65.0,
    };

    public static double MaxFrequency(this VoiceRange range) => range switch
    {
        VoiceRange.Low => 700.0,
        VoiceRange.High => 1000.0,
        _ => 900.0,
    };
}
