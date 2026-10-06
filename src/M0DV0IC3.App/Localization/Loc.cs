using System.Globalization;
using System.IO;
using System.Text.Json;

namespace M0DV0IC3.App.Localization;

public enum AppLanguage
{
    Spanish,
    English,
}

/// <summary>
/// Textos de la interfaz en español o en inglés. El texto en español es a la vez el texto de partida y la clave: cada
/// texto se busca en el diccionario inglés (<c>en.json</c>) y, si faltara, se queda en español en vez de romperse. Un
/// test comprueba que todos los textos de la app tienen su traducción.
/// <para>En XAML: <c>{l:T 'Texto'}</c>; en C#: <c>Loc.T("Texto")</c> y, con valores, <c>Loc.F("Grabando {0}", tiempo)</c>.
/// El idioma se elige en Ajustes y se aplica al reiniciar la app (los textos se leen al crear la ventana).</para>
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _english = [];

    public static AppLanguage Language { get; private set; } = AppLanguage.Spanish;

    public static bool IsEnglish => Language == AppLanguage.English;

    /// <summary>Elige el idioma de toda la app (y la cultura de los números: "1,5" o "1.5"). Antes de crear la ventana.</summary>
    public static void Initialize(AppLanguage language)
    {
        Language = language;
        _english = language == AppLanguage.English ? LoadEnglish() : [];
        var culture = CultureInfo.GetCultureInfo(language == AppLanguage.English ? "en-US" : "es-ES");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>El texto en el idioma de la app.</summary>
    public static string T(string spanish) =>
        IsEnglish && _english.TryGetValue(spanish, out string? english) ? english : spanish;

    /// <summary>Un texto con valores: <c>F("Grabando {0}", "0:12")</c>.</summary>
    public static string F(string spanishFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(spanishFormat), args);

    public static string Code(AppLanguage language) => language == AppLanguage.English ? "en" : "es";

    public static AppLanguage Parse(string? code) =>
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.English : AppLanguage.Spanish;

    /// <summary>El de Windows, para la primera vez que se abre la app: español si Windows está en español; si no, inglés.</summary>
    public static AppLanguage FromWindows() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "es" ? AppLanguage.Spanish : AppLanguage.English;

    private static Dictionary<string, string> LoadEnglish()
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("M0DV0IC3.App.Localization.en.json")
            ?? throw new FileNotFoundException("Falta el diccionario en.json dentro de la app.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
