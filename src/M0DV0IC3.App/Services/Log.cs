using System.IO;

namespace M0DV0IC3.App.Services;

/// <summary>Log de texto en %AppData%\M0DV0IC3\log.txt. Nunca lanza excepciones.</summary>
public static class Log
{
    private const long MaxSize = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message, Exception? error = null) => Write("AVISO", Format(message, error));

    public static void Error(string message, Exception? error = null) => Write("ERROR", Format(message, error));

    /// <summary>Si el log pasa de 1 MB, se guarda como log.old.txt y se empieza otro.</summary>
    public static void Rotate()
    {
        try
        {
            var file = new FileInfo(AppPaths.LogFile);
            if (file.Exists && file.Length > MaxSize)
                File.Move(file.FullName, Path.Combine(AppPaths.DataFolder, "log.old.txt"), overwrite: true);
        }
        catch
        {
            // Sin log no pasa nada grave.
        }
    }

    private static string Format(string message, Exception? error) => error is null ? message : $"{message}{Environment.NewLine}{error}";

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // El log nunca debe tumbar la app.
        }
    }
}
