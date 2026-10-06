using System.Text.Json;
using M0DV0IC3.Audio.Karaoke;

namespace M0DV0IC3.Tests;

// Las letras de las pruebas son inventadas.
public sealed class LyricsTests
{
    [Fact]
    public void Lrc_lines_are_parsed_sorted_and_headers_ignored()
    {
        const string lrc = "[ar:Nadie]\n[ti:Prueba]\n[00:12.50] segunda linea\n[00:03.20]primera linea\n[01:02.345] tercera\n[00:20.00][00:40.00] estribillo\n";
        var lines = LrcParser.Parse(lrc);

        Assert.Equal(5, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(3.2), lines[0].Time);
        Assert.Equal("primera linea", lines[0].Text);
        Assert.Equal("estribillo", lines[2].Text);
        Assert.Equal(TimeSpan.FromSeconds(40), lines[3].Time);
        Assert.Equal(TimeSpan.FromSeconds(62.345), lines[4].Time);
    }

    [Fact]
    public void Offset_tag_moves_the_lyrics()
    {
        var lines = LrcParser.Parse("[offset:+500]\n[00:10.00] uno");
        Assert.Equal(TimeSpan.FromSeconds(9.5), lines[0].Time);
    }

    [Fact]
    public void Current_line_follows_the_position()
    {
        var lyrics = new SongLyrics(LrcParser.Parse("[00:05.00] a\n[00:10.00] b\n[00:15.00] c"), true, false);
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(14.9)));
        Assert.Equal(2, lyrics.IndexAt(TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void Lrclib_answer_prefers_synced_lyrics_and_close_duration()
    {
        const string json = """
        [
          { "duration": 200, "syncedLyrics": null, "plainLyrics": "otra cancion" },
          { "duration": 181, "syncedLyrics": "[00:01.00] buena", "plainLyrics": "buena" },
          { "duration": 240, "syncedLyrics": "[00:01.00] larga", "plainLyrics": "larga" }
        ]
        """;
        using var doc = JsonDocument.Parse(json);
        var best = LrclibClient.PickBest(doc.RootElement, 180);
        Assert.NotNull(best);
        Assert.True(best.IsSynced);
        Assert.Equal("buena", best.Lines[0].Text);

        using var instrumental = JsonDocument.Parse("""{ "instrumental": true, "syncedLyrics": null, "plainLyrics": null }""");
        Assert.True(LrclibClient.FromJson(instrumental.RootElement)!.IsInstrumental);
    }

    // El User-Agent lleva producto y comentario: con ProductInfoHeaderValue.Parse la app ni arrancaba.
    [Fact]
    public void Client_accepts_a_user_agent_with_comment()
    {
        using var client = new LrclibClient("M0DV0IC3/1.2.0 (+https://github.com/Ayoubdeta/M0DV0IC3)");
    }

    [Theory]
    [InlineData("Cancion - Remastered 2011", "Cancion")]
    [InlineData("Cancion (feat. Alguien)", "Cancion")]
    [InlineData("Cancion [Live]", "Cancion")]
    [InlineData("Cancion", "Cancion")]
    public void Titles_are_cleaned_for_searching(string title, string expected) =>
        Assert.Equal(expected, LrclibClient.CleanTitle(title));
}
