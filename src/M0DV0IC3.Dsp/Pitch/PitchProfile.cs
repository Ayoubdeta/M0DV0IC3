namespace M0DV0IC3.Dsp.Pitch;

/// <summary>
/// Tono medio de la voz del usuario, aprendido mientras habla. Lo usan las voces con tono objetivo
/// (<see cref="Presets.VoicePreset.TargetPitchHz"/>): una voz de mujer necesita subir unos 5 semitonos desde una
/// voz de hombre aguda, pero más de 10 desde una grave. Lo comparten todas las cadenas de voz, así que al cambiar
/// de voz no hay que volver a aprenderlo, y la app lo guarda entre sesiones.
/// <para>Lo actualiza el hilo de audio; la UI solo lo lee (un double se lee y escribe de una vez en x64).</para>
/// </summary>
public sealed class PitchProfile
{
    /// <summary>Segundos de voz que hacen falta para fiarse de la media.</summary>
    public const double LearnSeconds = 0.3;

    // Al principio es una media de todo lo oído; después, una media móvil de unos 4 s de voz.
    private const double TimeConstantSeconds = 4.0;

    private double _logHz = Math.Log(130);
    private double _voicedSeconds;

    /// <summary>Tono medio en Hz (130 Hz hasta que se aprende).</summary>
    public double CenterHz => Math.Exp(_logHz);

    public bool IsLearned => _voicedSeconds >= LearnSeconds;

    /// <summary>Empieza con un valor ya conocido (el de la sesión anterior), como si se hubiera oído hablar un rato.</summary>
    public void Seed(double hz)
    {
        if (!double.IsFinite(hz) || hz < 40 || hz > 1000) return;
        _logHz = Math.Log(hz);
        _voicedSeconds = Math.Max(_voicedSeconds, TimeConstantSeconds / 2);
    }

    /// <summary>Añade el tono de un tramo sonoro de <paramref name="seconds"/> de duración.</summary>
    public void Add(double hz, double seconds)
    {
        if (!(seconds > 0) || !double.IsFinite(hz) || hz <= 0) return;
        double log = Math.Log(hz);
        // Una vez aprendido, los saltos de más de una octava casi siempre son errores del detector.
        if (IsLearned && Math.Abs(log - _logHz) > Math.Log(1.9)) return;
        _voicedSeconds += seconds;
        double tau = Math.Min(TimeConstantSeconds, _voicedSeconds);
        _logHz += seconds / tau * (log - _logHz);
    }

    public void Reset()
    {
        _logHz = Math.Log(130);
        _voicedSeconds = 0;
    }
}
