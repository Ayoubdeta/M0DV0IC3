using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

/// <summary>
/// La app en inglés: cada texto en español (que es la clave) tiene que estar en el diccionario en.json, con los mismos
/// huecos ({0}, {1}...), y el diccionario no debe guardar textos que ya no se usan. Los textos se buscan en el código:
/// <c>{l:T '...'}</c> en XAML, <c>Loc.T("...")</c> / <c>Loc.F("...")</c> en la app y <c>AudioText.T/F</c> en el audio,
/// más los nombres de las voces incluidas y de las notas del autotune.
/// </summary>
public sealed class LocalizationTests(ITestOutputHelper output)
{
    private static readonly Regex XamlKey = new(@"\{l:T '((?:[^'\\]|\\.)*)'\}", RegexOptions.Compiled);
    private static readonly Regex CodeKey = new(@"\b(?:Loc|AudioText)\.[TF]\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    [Fact]
    public void Every_text_has_its_English_translation()
    {
        var english = LoadEnglish();
        var keys = CollectKeys();
        var missing = keys.Where(k => !english.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        foreach (string key in missing) output.WriteLine($"FALTA: {JsonSerializer.Serialize(key)}");
        Assert.True(missing.Count == 0, $"Faltan {missing.Count} traducciones (ver la salida del test).");
    }

    [Fact]
    public void Translations_keep_the_same_placeholders_and_are_not_empty()
    {
        foreach (var (spanish, english) in LoadEnglish())
        {
            Assert.False(string.IsNullOrWhiteSpace(english), $"Traducción vacía: {spanish}");
            var a = Placeholder.Matches(spanish).Select(m => m.Groups[1].Value).Distinct().Order();
            var b = Placeholder.Matches(english).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(a.SequenceEqual(b), $"Huecos distintos en «{spanish}» → «{english}»");
            Assert.Equal(spanish.Split("**").Length, english.Split("**").Length);
        }
    }

    [Fact]
    public void The_dictionary_has_no_leftovers()
    {
        var keys = CollectKeys();
        var unused = LoadEnglish().Keys.Where(k => !keys.Contains(k)).ToList();
        foreach (string key in unused) output.WriteLine($"SOBRA: {JsonSerializer.Serialize(key)}");
        Assert.True(unused.Count == 0, $"Sobran {unused.Count} textos en en.json (ver la salida del test).");
    }

    [Fact]
    public void Texts_are_literals_so_they_can_be_found()
    {
        // Un texto interpolado ($"...") no se puede buscar en el diccionario: hay que usar Loc.F con {0}.
        var bad = SourceFiles("*.cs").SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line, @"\b(?:Loc|AudioText)\.[TF]\(\s*\$"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(bad);
    }

    private static Dictionary<string, string> LoadEnglish() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(SourceRoot(), "M0DV0IC3.App", "Localization", "en.json")))!;

    internal static HashSet<string> CollectKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in SourceFiles("*.xaml"))
        {
            foreach (Match m in XamlKey.Matches(File.ReadAllText(file)))
                keys.Add(WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1")));
        }
        foreach (string file in SourceFiles("*.cs"))
        {
            foreach (Match m in CodeKey.Matches(File.ReadAllText(file)))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
        }
        foreach (var voice in BuiltInVoices.All) keys.Add(voice.Name);
        foreach (string note in AutotuneNames.Notes) keys.Add(note);
        return keys;
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        new[] { "M0DV0IC3.App", "M0DV0IC3.Audio" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(SourceRoot(), project), pattern, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "M0DV0IC3.App"))) return src;
        }
        throw new DirectoryNotFoundException("No encuentro la carpeta src del repositorio.");
    }
}
