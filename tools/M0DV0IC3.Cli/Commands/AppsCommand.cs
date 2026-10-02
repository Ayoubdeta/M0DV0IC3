using M0DV0IC3.Audio.AppAudio;

namespace M0DV0IC3.Cli.Commands;

/// <summary>Lista las apps que se pueden transmitir por el micro ("Música por el micro"). Solo consulta.</summary>
internal static class AppsCommand
{
    public static int Run(string[] args)
    {
        CliArgs.Parse(args, [], []).ExpectPositional(0);
        if (!AppAudioStreamer.IsSupported)
        {
            Console.WriteLine("Este Windows no permite capturar el audio de una sola app (hace falta Windows 10 versión 2004 o posterior).");
            return 1;
        }

        var apps = AudioAppFinder.List();
        if (apps.Count == 0)
        {
            Console.WriteLine("No hay ninguna app con audio. Abre Spotify (o pon algo en el navegador) y vuelve a probar.");
            return 0;
        }

        var table = new TextTable("PID", "Ejecutable", "Nombre", "Estado").AlignRight(0);
        foreach (var app in apps) table.Add($"{app.ProcessId}", app.ExeName, app.DisplayName, app.IsPlaying ? "sonando" : "en silencio");
        table.Print();
        return 0;
    }

    /// <summary>Busca una app por ejecutable o por parte de su nombre ("spotify", "brave").</summary>
    public static AudioApp Find(string spec)
    {
        var apps = AudioAppFinder.List();
        return apps.FirstOrDefault(a => a.ExeName.Equals(spec, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(a => a.DisplayName.Contains(spec, StringComparison.OrdinalIgnoreCase) || a.ExeName.Contains(spec, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"No hay ninguna app con audio que se llame '{spec}'. Mira las disponibles con 'apps'.");
    }
}
