using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace M0DV0IC3.Audio.Karaoke;

/// <summary>Una línea de la letra y el momento de la canción en que empieza.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Letra de una canción: sincronizada (cada línea con su momento) o solo el texto.</summary>
public sealed record SongLyrics(IReadOnlyList<LyricLine> Lines, bool IsSynced, bool IsInstrumental)
{
    /// <summary>Índice de la línea que toca en <paramref name="position"/>, o -1 antes de la primera.</summary>
    public int IndexAt(TimeSpan position)
    {
        if (!IsSynced) return -1;
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }
}

/// <summary>Lee letras en formato LRC: "[01:23.45] texto". Una línea puede tener varias marcas de tiempo.</summary>
public static partial class LrcParser
{
    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        var lines = new List<LyricLine>();
        var offset = TimeSpan.Zero;
        foreach (string raw in lrc.Split('\n'))
        {
            string line = raw.Trim();
            var offsetMatch = OffsetTag().Match(line);
            if (offsetMatch.Success)
            {
                // [offset:+500] adelanta la letra medio segundo.
                offset = -TimeSpan.FromMilliseconds(int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var times = new List<TimeSpan>();
            int pos = 0;
            Match match;
            while ((match = TimeTag().Match(line, pos)).Success && match.Index == pos)
            {
                int minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double seconds = double.Parse(match.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                times.Add(TimeSpan.FromSeconds(minutes * 60 + seconds));
                pos = match.Index + match.Length;
            }
            if (times.Count == 0) continue; // [ar:...], [ti:...] y otras cabeceras

            string text = line[pos..].Trim();
            foreach (var time in times) lines.Add(new LyricLine(Max(TimeSpan.Zero, time + offset), text));
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>Letra sin tiempos: una línea por renglón.</summary>
    public static IReadOnlyList<LyricLine> ParsePlain(string text) =>
        text.Split('\n').Select(l => new LyricLine(TimeSpan.Zero, l.TrimEnd('\r'))).ToList();

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();
}

/// <summary>
/// Busca la letra en LRCLIB (https://lrclib.net), una base de datos libre de letras sincronizadas que no pide
/// cuenta ni clave. Solo envía el artista, el título, el álbum y la duración. La letra no se guarda en disco.
/// </summary>
public sealed partial class LrclibClient : IDisposable
{
    private const string BaseUrl = "https://lrclib.net/api/";

    private readonly HttpClient _http;

    public LrclibClient(string userAgent, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
        // LRCLIB pide que cada app se identifique ("Nombre/versión (web)": producto y comentario).
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    /// <summary>La letra, o null si no está en LRCLIB. Lanza <see cref="HttpRequestException"/> si no hay conexión.</summary>
    public async Task<SongLyrics?> FindAsync(string artist, string title, string album, TimeSpan duration, CancellationToken cancellation = default)
    {
        int seconds = (int)Math.Round(duration.TotalSeconds);
        string query = $"get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}"
            + (album.Length > 0 ? $"&album_name={Uri.EscapeDataString(album)}" : "")
            + (seconds > 0 ? $"&duration={seconds}" : "");
        using (var response = await _http.GetAsync(BaseUrl + query, cancellation))
        {
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                if (FromJson(doc.RootElement) is { } exact) return exact;
            }
            else if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
            }
        }

        // Spotify añade a veces " - Remastered 2011" o "(feat. ...)": se busca también sin eso, por duración parecida.
        foreach (string name in new[] { title, CleanTitle(title) }.Distinct())
        {
            string search = $"search?track_name={Uri.EscapeDataString(name)}&artist_name={Uri.EscapeDataString(artist)}";
            using var response = await _http.GetAsync(BaseUrl + search, cancellation);
            if (!response.IsSuccessStatusCode) continue;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (PickBest(doc.RootElement, seconds) is { } found) return found;
        }
        return null;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Lee una respuesta de LRCLIB: prefiere la letra sincronizada.</summary>
    public static SongLyrics? FromJson(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        if (item.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True)
            return new SongLyrics([], false, true);
        if (item.TryGetProperty("syncedLyrics", out var synced) && synced.GetString() is { Length: > 0 } lrc)
        {
            var lines = LrcParser.Parse(lrc);
            if (lines.Count > 0) return new SongLyrics(lines, true, false);
        }
        if (item.TryGetProperty("plainLyrics", out var plain) && plain.GetString() is { Length: > 0 } text)
            return new SongLyrics(LrcParser.ParsePlain(text), false, false);
        return null;
    }

    /// <summary>De los resultados de una búsqueda, el de duración más parecida (±3 s), mejor si está sincronizado.</summary>
    public static SongLyrics? PickBest(JsonElement results, int seconds)
    {
        if (results.ValueKind != JsonValueKind.Array) return null;
        SongLyrics? best = null;
        double bestScore = double.MaxValue;
        foreach (var item in results.EnumerateArray())
        {
            double difference = item.TryGetProperty("duration", out var d) && d.TryGetDouble(out double value) ? Math.Abs(value - seconds) : 0;
            if (seconds > 0 && difference > 3) continue;
            if (FromJson(item) is not { } lyrics) continue;
            double score = difference + (lyrics.IsSynced ? 0 : 10);
            if (score < bestScore)
            {
                bestScore = score;
                best = lyrics;
            }
        }
        return best;
    }

    /// <summary>"Canción - Remastered 2011 (feat. Alguien)" → "Canción".</summary>
    public static string CleanTitle(string title)
    {
        string clean = FeaturingOrBrackets().Replace(title, "");
        int dash = clean.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) clean = clean[..dash];
        return clean.Trim();
    }

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex FeaturingOrBrackets();
}
