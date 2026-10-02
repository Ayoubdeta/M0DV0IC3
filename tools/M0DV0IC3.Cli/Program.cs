using System.Text;
using M0DV0IC3.Cli.Commands;

namespace M0DV0IC3.Cli;

internal static class Program
{
    private const string Usage = """
        m0dv0ic3-cli: herramientas de desarrollo de M0DV0IC3 (afinar voces sin micro y probar dispositivos).

        Uso: m0dv0ic3-cli <comando> [opciones]

        Comandos:
          voices
              Lista las voces incluidas (tono, formantes, efectos y si usan PSOLA).
          devices
              Lista los dispositivos de captura y de salida: formato de mezcla y periodos WASAPI. Solo consulta.
          synth <salida.wav> [--seconds 8] [--f0 130]
              Genera una señal de prueba parecida a la voz (48 kHz mono, float 32), con pico de -6 dBFS.
          process <entrada> <salida.wav> --voice <id|normal> [--denoise] [--range low|medium|high]
                  [--gate <dB>] [--block <muestras>] [--monitor]
              Procesa un archivo con el mismo pipeline que la app, en bloques de --block muestras (480 = 10 ms).
              --monitor activa "Escucharme" y guarda también la mezcla de auriculares en <salida>.monitor.wav.
          analyze <archivo> [--range low|medium|high]
              Pista de f0 (YIN) cada 100 ms, nivel, resumen y centroide espectral.
          bench [--seconds 30] [--block 480] [--range medium]
              Coste de CPU y latencia de cada voz. Compílalo en Release para que los números valgan.
          probe [--input <id|default>] [--output <id|default|none>] [--seconds 3] [--exclusive] [--no-lowlatency]
              Abre los streams WASAPI y mide sus callbacks. Lo capturado solo se mide (nunca se guarda)
              y la salida reproduce silencio. Los dispositivos se eligen por ID o por una parte del nombre.
          engine [--input <id|default>] [--output <id|default>] [--monitor <id|none>] [--seconds 5]
                 [--voice mujer] [--denoise] [--exclusive] [--no-lowlatency] [--margin 2] [--app spotify]
              Arranca el motor completo de la app con dispositivos reales y mide latencia y cortes cada segundo.
              Va siempre silenciado: el audio se procesa entero, pero a las salidas solo llegan ceros.
              --app mezcla también el audio de esa app ("Música por el micro") y mide su nivel.
          apps
              Lista las apps con audio que se pueden transmitir por el micro (PID, ejecutable y si suenan).
          help
              Muestra esta ayuda.

        Rangos de voz: low (50-700 Hz), medium (65-900 Hz, por defecto), high (120-1000 Hz).

        Ejemplos:
          m0dv0ic3-cli synth %TEMP%\voz.wav --f0 120
          m0dv0ic3-cli process %TEMP%\voz.wav %TEMP%\mujer.wav --voice mujer
          m0dv0ic3-cli analyze %TEMP%\mujer.wav
          m0dv0ic3-cli probe --seconds 2 --output default
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        string[] rest = args[1..];
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "voices" => VoicesCommand.Run(rest),
                "devices" => DevicesCommand.Run(rest),
                "synth" => SynthCommand.Run(rest),
                "process" => ProcessCommand.Run(rest),
                "analyze" => AnalyzeCommand.Run(rest),
                "bench" => BenchCommand.Run(rest),
                "probe" => ProbeCommand.Run(rest),
                "engine" => EngineCommand.Run(rest),
                "apps" => AppsCommand.Run(rest),
                _ => throw new CliException($"Comando desconocido: '{args[0]}'. Usa 'help' para ver los comandos.", 2),
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error inesperado ({ex.GetType().Name}): {ex.Message}");
            return 1;
        }
    }
}
