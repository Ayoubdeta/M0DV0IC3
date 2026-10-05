using System.Text.RegularExpressions;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Dsp;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

/// <summary>Los sonidos incluidos (src/M0DV0IC3.App/Sonidos) y su catálogo (BuiltInSounds.cs).</summary>
public sealed partial class SoundTests(ITestOutputHelper output)
{
    private static readonly string AppFolder = Path.Combine(FindRepositoryRoot(), "src", "M0DV0IC3.App");
    private static readonly string SoundsFolder = Path.Combine(AppFolder, "Sonidos");

    // Windows no trae decodificador de OGG Vorbis: si SoundLoader dependiera de Media Foundation, fallaría.
    [Fact]
    public void Every_included_sound_decodes_and_is_short()
    {
        var files = Directory.GetFiles(SoundsFolder, "*.ogg", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            var clip = SoundLoader.Load(file, "test", Path.GetFileName(file));
            float peak = DspMath.Peak(clip.Samples);
            output.WriteLine($"{Path.GetRelativePath(SoundsFolder, file)}: {clip.Duration.TotalSeconds:0.00} s, pico {DspMath.GainToDb(peak):0.0} dBFS");
            Assert.InRange(clip.Duration.TotalSeconds, 0.05, 5);
            Assert.InRange(peak, 0.1f, 1.5f);
        }
    }

    [Fact]
    public void Catalog_and_files_match()
    {
        string catalog = File.ReadAllText(Path.Combine(AppFolder, "Models", "BuiltInSounds.cs"));
        var listed = CatalogPath().Matches(catalog).Select(m => m.Groups[1].Value).ToList();
        var files = Directory.GetFiles(SoundsFolder, "*.ogg", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SoundsFolder, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Empty(listed.Except(files));
        Assert.Empty(files.Except(listed));
    }

    [GeneratedRegex("""new\("([a-z0-9/\-]+\.ogg)",""")]
    private static partial Regex CatalogPath();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "M0DV0IC3.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No encuentro la raíz del repositorio (M0DV0IC3.slnx).");
    }
}
