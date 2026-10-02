using System.IO;

namespace M0DV0IC3.App.Services;

/// <summary>Carpeta de datos de la app: %AppData%\M0DV0IC3.</summary>
public static class AppPaths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M0DV0IC3");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string LogFile => Path.Combine(DataFolder, "log.txt");

    /// <summary>Copias de los sonidos del soundboard ({guid}{extensión}).</summary>
    public static string SoundsFolder => Path.Combine(DataFolder, "sounds");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(SoundsFolder);
    }
}
