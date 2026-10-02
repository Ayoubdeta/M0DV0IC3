using System.Text.Json.Serialization;
using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Dsp.Presets;

/// <summary>
/// Una voz: todos los parámetros de la cadena de efectos. Es inmutable (los cambios se hacen con
/// <c>with</c>) y se serializa a JSON para las voces personalizadas.
/// Un valor de 0 en un efecto significa que ese efecto está apagado.
/// </summary>
public sealed record VoicePreset
{
    public string Id { get; init; } = "custom";
    public string Name { get; init; } = "Personalizada";
    public string Icon { get; init; } = "🎙";
    public bool IsBuiltIn { get; init; }

    /// <summary>Cambio de tono en semitonos (-12..+12).</summary>
    public double PitchSemitones { get; init; }

    /// <summary>Desplazamiento de formantes: menor que 1 = más grande u oscuro, mayor que 1 = más pequeño o brillante.</summary>
    public double FormantRatio { get; init; } = 1.0;

    /// <summary>Si es mayor que 0, la voz se vuelve monótona a esta frecuencia (robot).</summary>
    public double RobotHz { get; init; }

    /// <summary>Autotune: escala a la que se ajusta la voz (<see cref="AutotuneScale.Off"/> = apagado).</summary>
    public AutotuneScale AutotuneScale { get; init; }

    /// <summary>Tónica de la escala del autotune: 0 = Do … 9 = La … 11 = Si.</summary>
    public int AutotuneKey { get; init; }

    /// <summary>Velocidad del autotune en ms: 0 = instantáneo (el efecto robótico de la música urbana), 50-200 = natural.</summary>
    public double AutotuneRetuneMs { get; init; }

    /// <summary>
    /// Exagerar la melodía 0..1: amplía las subidas y bajadas de la voz (hasta 2,5×) antes de afinar, para que al
    /// hablar suene cantado. Para cantar encima de una canción, mejor 0.
    /// </summary>
    public double AutotuneExaggeration { get; init; }

    /// <summary>Vibrato: desviación máxima del tono en semitonos (0 = apagado).</summary>
    public double VibratoSemitones { get; init; }

    /// <summary>Velocidad del vibrato, en oscilaciones por segundo.</summary>
    public double VibratoHz { get; init; } = 5.5;

    /// <summary>Voz invertida: cada trozo de esta longitud se reproduce al revés (0 = apagado). Añade ese retardo.</summary>
    public double ReverseMs { get; init; }

    /// <summary>Susurro 0..1: cambia la vibración de la voz por un soplo con los mismos formantes.</summary>
    public double WhisperMix { get; init; }

    /// <summary>Aire / respiración 0..1: soplo suave que sigue la voz (más femenina o susurrada).</summary>
    public double Breathiness { get; init; }

    public double RingModHz { get; init; }
    public double RingModMix { get; init; }

    public double CombMs { get; init; }
    public double CombFeedback { get; init; }
    public double CombMix { get; init; }

    /// <summary>Saturación 0..1.</summary>
    public double Distortion { get; init; }

    public double BitcrushRateHz { get; init; }
    public int BitcrushBits { get; init; }

    public double HighPassHz { get; init; }
    public double LowPassHz { get; init; }

    /// <summary>Realce o atenuación de presencia (campana en 1,8 kHz), en dB.</summary>
    public double PresenceDb { get; init; }

    /// <summary>Estantería de agudos en 3,5 kHz, en dB.</summary>
    public double HighShelfDb { get; init; }

    public double ChorusMix { get; init; }

    public double FlangerMix { get; init; }

    /// <summary>Barridos por segundo del flanger.</summary>
    public double FlangerHz { get; init; } = 0.25;

    public double EchoMs { get; init; }
    public double EchoFeedback { get; init; }
    public double EchoMix { get; init; }

    public double ReverbMix { get; init; }
    public double ReverbSize { get; init; } = 0.5;

    public double OutputGainDb { get; init; }

    /// <summary>Hace falta PSOLA (cambio de tono, formantes, robot o autotune), que añade algo de latencia.</summary>
    [JsonIgnore]
    public bool UsesPitch =>
        Math.Abs(PitchSemitones) > 0.01 || Math.Abs(FormantRatio - 1.0) > 0.005 || RobotHz > 0 || AutotuneScale != AutotuneScale.Off;

    /// <summary>La voz invertida necesita su propio buffer y añade un trozo entero de retardo.</summary>
    [JsonIgnore]
    public bool UsesReverse => ReverseMs > 0;

    public static VoicePreset Neutral { get; } = new() { Id = "normal", Name = "Normal", Icon = "🎙", IsBuiltIn = true };
}
