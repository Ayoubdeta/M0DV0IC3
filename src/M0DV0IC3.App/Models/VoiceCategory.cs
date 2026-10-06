using M0DV0IC3.App.Localization;

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
            or "coro" or "vocoder" or "walkie" or "astronauta" or "8bits" or "vinilo" or "lata" or "mascarilla" => VoiceCategory.Effects,
        "lejana" or "cueva" or "reverb" or "delay" or "chorus" or "flanger" or "estadio" or "ducha" or "catedral"
            or "megafonia" => VoiceCategory.Ambience,
        _ => VoiceCategory.Characters,
    };

    public static string Title(VoiceCategory category) => category switch
    {
        VoiceCategory.Autotune => "Autotune",
        VoiceCategory.Characters => Loc.T("Personajes"),
        VoiceCategory.Effects => Loc.T("Efectos"),
        VoiceCategory.Ambience => Loc.T("Ambientes"),
        _ => Loc.T("Mis voces"),
    };

    public static string Subtitle(VoiceCategory category) => category switch
    {
        VoiceCategory.Autotune => Loc.T("Tu voz afinada como en las canciones"),
        VoiceCategory.Characters => Loc.T("Cambia quién parece que habla"),
        VoiceCategory.Effects => Loc.T("Transforma el sonido de tu voz"),
        VoiceCategory.Ambience => Loc.T("Lleva tu voz a otro sitio"),
        _ => Loc.T("Las que has creado o duplicado"),
    };
}
