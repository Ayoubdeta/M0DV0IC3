namespace M0DV0IC3.App.Models;

/// <summary>Grupos de la pestaña Voces, en el orden en que se muestran (y en el que cuentan los atajos Voz 1..9).</summary>
public enum VoiceCategory
{
    Autotune,
    Characters,
    Effects,
    Ambience,
    Custom,
}

public static class VoiceCategories
{
    public static IReadOnlyList<VoiceCategory> BuiltInOrder { get; } =
        [VoiceCategory.Autotune, VoiceCategory.Characters, VoiceCategory.Effects, VoiceCategory.Ambience];

    /// <summary>Grupo de una voz incluida, por su id. Las que no se conocen van a Personajes.</summary>
    public static VoiceCategory Of(string voiceId) => voiceId switch
    {
        "autotune" or "autotune-mujer" or "autotune-cantar" => VoiceCategory.Autotune,
        "susurro" or "invertida" or "agua" or "espacial" or "radio" or "telefono" or "megafono" or "distorsion"
            or "coro" or "vocoder" or "walkie" or "astronauta" => VoiceCategory.Effects,
        "lejana" or "cueva" or "reverb" or "delay" or "chorus" or "flanger" => VoiceCategory.Ambience,
        _ => VoiceCategory.Characters,
    };

    public static string Title(VoiceCategory category) => category switch
    {
        VoiceCategory.Autotune => "Autotune",
        VoiceCategory.Characters => "Personajes",
        VoiceCategory.Effects => "Efectos",
        VoiceCategory.Ambience => "Ambientes",
        _ => "Mis voces",
    };

    public static string Subtitle(VoiceCategory category) => category switch
    {
        VoiceCategory.Autotune => "Tu voz afinada como en las canciones",
        VoiceCategory.Characters => "Cambia quién parece que habla",
        VoiceCategory.Effects => "Transforma el sonido de tu voz",
        VoiceCategory.Ambience => "Lleva tu voz a otro sitio",
        _ => "Las que has creado o duplicado",
    };
}
