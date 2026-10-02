using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using M0DV0IC3.App.Models;

namespace M0DV0IC3.App.Services;

/// <summary>
/// Carga y guarda %AppData%\M0DV0IC3\settings.json. Los cambios se agrupan y se escriben a los 500 ms
/// (<see cref="ScheduleSave"/>); al salir se llama a <see cref="SaveNow"/>. Úsalo solo desde el hilo de la UI.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // Los iconos son emojis: mejor legibles que escapados.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DispatcherTimer _saveTimer;

    private SettingsService(AppSettings settings)
    {
        Current = settings;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public AppSettings Current { get; }

    /// <summary>Carga los ajustes. Si el archivo no existe o está roto, empieza con los valores por defecto.</summary>
    public static SettingsService Load()
    {
        AppSettings? settings = null;
        string path = AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn("settings.json no se pudo leer; se usan los valores por defecto", ex);
            try
            {
                File.Copy(path, Path.Combine(AppPaths.DataFolder, $"settings.roto-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);
            }
            catch
            {
                // Si ni siquiera se puede copiar, se sobrescribirá al guardar.
            }
        }

        settings ??= new AppSettings();
        settings.Normalize();
        return new SettingsService(settings);
    }

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            // Se escribe a un temporal y se reemplaza, para no dejar el archivo a medias si se va la luz.
            string temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error("No se pudieron guardar los ajustes", ex);
        }
    }
}
