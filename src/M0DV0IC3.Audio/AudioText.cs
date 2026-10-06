using System.Globalization;

namespace M0DV0IC3.Audio;

/// <summary>
/// Los mensajes que este proyecto enseña al usuario (errores de dispositivos) salen en el idioma de la app: la app
/// pone aquí su traductor al arrancar. Sin traductor (CLI, tests) se quedan en español.
/// </summary>
public static class AudioText
{
    /// <summary>Del texto en español al del idioma de la app.</summary>
    public static Func<string, string> Translate { get; set; } = text => text;

    public static string T(string spanish) => Translate(spanish);

    public static string F(string spanishFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Translate(spanishFormat), args);
}
