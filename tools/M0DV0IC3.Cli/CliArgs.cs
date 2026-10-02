using System.Globalization;
using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Cli;

/// <summary>Error que se muestra tal cual al usuario. Código 2 = error de uso, 1 = fallo al ejecutar.</summary>
internal sealed class CliException(string message, int exitCode = 1) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>Argumentos de un comando: posicionales, opciones con valor (<c>--x 3</c> o <c>--x=3</c>) y flags (<c>--x</c>).</summary>
internal sealed class CliArgs
{
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private CliArgs()
    {
    }

    public IReadOnlyList<string> Positional => _positional;

    /// <param name="options">Opciones que llevan valor.</param>
    /// <param name="flags">Opciones sin valor.</param>
    public static CliArgs Parse(IReadOnlyList<string> args, string[] options, string[] flags)
    {
        var result = new CliArgs();
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
            {
                result._positional.Add(arg);
                continue;
            }

            string name = arg[2..];
            string? value = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }

            if (flags.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value is not null) throw new CliException($"La opción --{name} no lleva valor.", 2);
                result._flags.Add(name);
            }
            else if (options.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value is null)
                {
                    // El siguiente token es el valor aunque empiece por '-' (por ejemplo, --gate -45).
                    if (i + 1 >= args.Count) throw new CliException($"Falta el valor de --{name}.", 2);
                    value = args[++i];
                }
                result._values[name] = value;
            }
            else
            {
                throw new CliException($"Opción desconocida: --{name}", 2);
            }
        }
        return result;
    }

    public void ExpectPositional(int count)
    {
        if (_positional.Count > count)
            throw new CliException($"Sobran argumentos: {string.Join(' ', _positional.Skip(count))}", 2);
    }

    public string Required(int index, string what)
    {
        if (index >= _positional.Count) throw new CliException($"Falta {what}.", 2);
        return _positional[index];
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public string Get(string name, string defaultValue) => _values.GetValueOrDefault(name) ?? defaultValue;

    public double GetDouble(string name, double defaultValue, double min, double max)
    {
        string? text = Get(name);
        if (text is null) return defaultValue;
        if (!TryParseNumber(text, out double value) || !double.IsFinite(value))
            throw new CliException($"--{name}: '{text}' no es un número.", 2);
        if (value < min || value > max)
            throw new CliException($"--{name} debe estar entre {min} y {max} (es {value}).", 2);
        return value;
    }

    public int GetInt(string name, int defaultValue, int min, int max)
    {
        string? text = Get(name);
        if (text is null) return defaultValue;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new CliException($"--{name}: '{text}' no es un número entero.", 2);
        if (value < min || value > max)
            throw new CliException($"--{name} debe estar entre {min} y {max} (es {value}).", 2);
        return value;
    }

    public VoiceRange GetRange()
    {
        string text = Get("range", "medium");
        return text.ToLowerInvariant() switch
        {
            "low" or "baja" or "grave" => VoiceRange.Low,
            "medium" or "media" => VoiceRange.Medium,
            "high" or "alta" or "aguda" => VoiceRange.High,
            _ => throw new CliException($"--range: '{text}' no es válido (usa low, medium o high).", 2),
        };
    }

    public static string RangeName(VoiceRange range) => range.ToString().ToLowerInvariant();

    /// <summary>Acepta tanto punto como coma decimal.</summary>
    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
