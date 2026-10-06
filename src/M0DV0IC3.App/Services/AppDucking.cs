using System.Windows.Threading;
using M0DV0IC3.Audio.AppAudio;

namespace M0DV0IC3.App.Services;

/// <summary>
/// Mientras la canción modificada (sin voz en el karaoke, o con otra voz para el cantante) suena en tus cascos, la app
/// original (Spotify) se baja al 0,1 % en Windows para que no se oiga dos veces. Windows captura el audio de la app ya
/// con su volumen, así que la captura lo compensa (<see cref="AppAudioStreamer.CaptureGain"/>).
/// <para>Windows guarda el volumen de cada app entre sesiones: el original se apunta en los ajustes, con el ejecutable,
/// y si M0DV0IC3 se cierra de golpe, se le devuelve en cuanto la app vuelva a estar abierta.</para>
/// <para>Cada 2 s: mientras está bajada, se vuelve a bajar (la app puede abrir sesiones de audio nuevas, o tú cambiar
/// su volumen); si no, se mira si quedó algún volumen por devolver. Bajar y devolver nunca se cruzan: si se cruzaran,
/// la app podría quedarse bajada.</para>
/// Úsalo desde el hilo de la UI: el mezclador de Windows se toca en segundo plano y los ajustes, en la UI.
/// </summary>
public sealed class AppDucking
{
    private const string DefaultExe = "Spotify";

    private readonly SettingsService _settings;
    private readonly AppAudioStreamer _streamer;
    private readonly AppVolumeDucker _ducker = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _exe;

    public AppDucking(SettingsService settings, AppAudioStreamer streamer)
    {
        _settings = settings;
        _streamer = streamer;
        _timer.Tick += async (_, _) => await OnTickAsync();
        _timer.Start();
    }

    /// <summary>Hay una app bajada (o a punto de estarlo).</summary>
    public bool IsActive => _exe is not null;

    private bool HasPendingRestore => _settings.Current.KaraokeRestoreVolume > 0;

    private string PendingExe => _settings.Current.DuckedAppExe ?? DefaultExe;

    /// <summary>Baja esta app ("Spotify") y compensa la captura.</summary>
    public Task DuckAsync(string exeName)
    {
        _exe = exeName;
        return RunAsync(DuckNowAsync);
    }

    /// <summary>Devuelve a la app su volumen y deja la captura como estaba.</summary>
    public Task ReleaseAsync()
    {
        _exe = null;
        _streamer.CaptureGain = 1f;
        return RunAsync(RestoreNowAsync);
    }

    /// <summary>Al salir de M0DV0IC3: la app vuelve a su volumen ya, sin esperar.</summary>
    public void Shutdown()
    {
        _timer.Stop();
        _exe = null;
        if (!HasPendingRestore || AudioAppFinder.FindByExe(PendingExe) is not { } app) return;
        try
        {
            _ducker.Restore(app.ProcessId, (float)_settings.Current.KaraokeRestoreVolume);
            ClearPending();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo restaurar el volumen de la app al salir", ex);
        }
    }

    private async Task OnTickAsync()
    {
        if (_gate.CurrentCount == 0) return;
        if (IsActive) await RunAsync(DuckNowAsync);
        else if (HasPendingRestore) await RunAsync(RestoreNowAsync);
    }

    private async Task RunAsync(Func<Task> operation)
    {
        await _gate.WaitAsync();
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo cambiar el volumen de la app en Windows", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DuckNowAsync()
    {
        if (_exe is not { } exe || AudioAppFinder.FindByExe(exe) is not { } app) return;
        bool ducked = await Task.Run(() =>
        {
            _ducker.Duck(app.ProcessId);
            return _ducker.IsDucked;
        });
        if (!ducked) return;
        if (_exe is null)
        {
            // Se pidió devolverlo mientras se bajaba.
            await Task.Run(() => _ducker.Restore(app.ProcessId));
            return;
        }
        _streamer.CaptureGain = _ducker.OriginalVolume / AppVolumeDucker.DuckedLevel;
        if (Math.Abs(_settings.Current.KaraokeRestoreVolume - _ducker.OriginalVolume) > 0.001 || PendingExe != exe)
        {
            _settings.Current.KaraokeRestoreVolume = _ducker.OriginalVolume;
            _settings.Current.DuckedAppExe = exe;
            _settings.SaveNow();
        }
    }

    /// <summary>
    /// Devuelve el volumen apuntado. Si la app está cerrada, se queda apuntado y se devuelve cuando vuelva a abrirse
    /// (antes se olvidaba, y Spotify se podía quedar al 0,1 %).
    /// </summary>
    private async Task RestoreNowAsync()
    {
        if (_exe is not null || !HasPendingRestore || AudioAppFinder.FindByExe(PendingExe) is not { } app) return;
        float volume = (float)_settings.Current.KaraokeRestoreVolume;
        await Task.Run(() => _ducker.Restore(app.ProcessId, volume));
        if (_exe is null) ClearPending();
    }

    private void ClearPending()
    {
        _settings.Current.KaraokeRestoreVolume = 0;
        _settings.Current.DuckedAppExe = null;
        _settings.SaveNow();
    }
}
