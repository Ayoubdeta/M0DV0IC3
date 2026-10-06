using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Localization;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.AppAudio;
using M0DV0IC3.Audio;
using M0DV0IC3.Dsp.Presets;
using M0DV0IC3.Dsp;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Pestaña Música: transmitir por el micro lo que suena en una app (Spotify, el navegador...) y, si quieres, con otra
/// voz para el cantante (<see cref="SongVoiceChanger"/>): entonces la canción cambiada suena también en tus cascos y la
/// app se baja en Windows (<see cref="AppDucking"/>) para que no la oigas dos veces.
/// La app elegida, el volumen y la voz del cantante se guardan; la transmisión no, para que nunca empiece sola al abrir la app.
/// </summary>
public sealed partial class MusicViewModel : ObservableObject
{
    private const double MeterFloorDb = -60;

    private readonly SettingsService _settings;
    private readonly AppAudioStreamer _streamer;
    private readonly VoicePipeline _pipeline;
    private readonly VoicesViewModel _voices;
    private readonly AppDucking _ducking;
    private readonly Func<bool> _hasMonitor;
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _syncing;
    private bool _songVoiceBusy;
    private bool _startedForSongVoice;
    private bool _refreshing;
    private double _levelDb = MeterFloorDb;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    [NotifyPropertyChangedFor(nameof(StreamingName))]
    private AudioApp? _selectedApp;

    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private double _level;

    /// <summary>«Voz del cantante»: no se guarda, para que nunca empiece sola al abrir la app.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMonitorWarning))]
    private bool _songVoiceOn;

    /// <summary>El karaoke está puesto: quitar la voz y cambiarla no van a la vez.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeSongVoice))]
    private bool _karaokeActive;

    public MusicViewModel(SettingsService settings, AudioService audio, VoicesViewModel voices, AppDucking ducking, Func<bool> hasMonitor)
    {
        _settings = settings;
        _streamer = audio.AppAudio;
        _pipeline = audio.Pipeline;
        _voices = voices;
        _ducking = ducking;
        _hasMonitor = hasMonitor;
        _pipeline.AppAudioGain = DspMath.DbToGain(settings.Current.AppAudioVolumeDb);
        ApplySongVoice();
        _voices.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(VoicesViewModel.HoldVoiceChoices)) return;
            OnPropertyChanged(nameof(SongVoiceChoices));
            OnPropertyChanged(nameof(SongVoice));
        };
        _voices.PresetEdited += (_, card) =>
        {
            if (card == SongVoice) ApplySongVoice(liveEdit: true);
        };

        var dispatcher = Dispatcher.CurrentDispatcher;
        _streamer.Faulted += (_, error) => dispatcher.BeginInvoke(() => OnStreamerFaulted(error));
        _watchTimer.Tick += (_, _) => OnWatchTick();
        if (IsSupported) Refresh();
    }

    public ObservableCollection<AudioApp> Apps { get; } = [];

    /// <summary>Windows 10 versión 2004 o posterior.</summary>
    public bool IsSupported => AppAudioStreamer.IsSupported;

    public double VolumeDb
    {
        get => _settings.Current.AppAudioVolumeDb;
        set
        {
            value = Math.Clamp(Math.Round(value), -40, 6);
            if (_settings.Current.AppAudioVolumeDb == value) return;
            _settings.Current.AppAudioVolumeDb = value;
            _settings.ScheduleSave();
            _pipeline.AppAudioGain = DspMath.DbToGain(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{VolumeDb:+0;-0;0} dB";

    /// <summary>Las voces que se le pueden poner al cantante (las mismas que la tuya).</summary>
    public IReadOnlyList<VoiceCardViewModel> SongVoiceChoices => _voices.HoldVoiceChoices;

    /// <summary>La voz del cantante (Ardilla de serie).</summary>
    public VoiceCardViewModel? SongVoice
    {
        get => _voices.AllCards.FirstOrDefault(c => c.Id == _settings.Current.SongVoiceId);
        set
        {
            if (value is null || value.Id == _settings.Current.SongVoiceId) return;
            _settings.Current.SongVoiceId = value.Id;
            _settings.ScheduleSave();
            OnPropertyChanged();
            ApplySongVoice();
        }
    }

    public bool CanChangeSongVoice => IsSupported && !KaraokeActive;

    /// <summary>La canción cambiada va a tus cascos: si no los has elegido, no la oyes.</summary>
    public bool ShowMonitorWarning => SongVoiceOn && !_hasMonitor();

    public void RefreshMonitorWarning() => OnPropertyChanged(nameof(ShowMonitorWarning));

    /// <summary>La app que se transmite ("Spotify"), para la barra de estado; null si no se transmite nada.</summary>
    public string? StreamingName => IsStreaming ? (_streamer.Current ?? SelectedApp)?.DisplayName : null;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private void Toggle() => IsStreaming = !IsStreaming;

    private bool CanToggle() => IsSupported && (SelectedApp is not null || IsStreaming);

    /// <summary>
    /// Transmite esta app (por su ejecutable, "Spotify"), aunque estuviera transmitiendo otra. Lo usa el karaoke.
    /// Devuelve false si la app no está abierta o no se pudo capturar (el motivo queda en <see cref="Message"/>).
    /// </summary>
    public async Task<bool> StreamAppAsync(string exeName)
    {
        Refresh();
        var app = Apps.FirstOrDefault(a => a.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            Message = Loc.F("{0} no está abierto.", exeName);
            return false;
        }
        if (IsStreaming && string.Equals(_streamer.Current?.ExeName, exeName, StringComparison.OrdinalIgnoreCase)) return true;

        _refreshing = true;
        _syncing = true;
        try
        {
            SelectedApp = app;
            IsStreaming = true;
        }
        finally
        {
            _refreshing = false;
            _syncing = false;
        }
        await StartAsync();
        return _streamer.IsRunning;
    }

    [RelayCommand]
    private void Refresh()
    {
        IReadOnlyList<AudioApp> apps;
        try
        {
            apps = AudioAppFinder.List();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron listar las apps con audio", ex);
            apps = [];
        }

        string? wanted = SelectedApp?.ExeName ?? _settings.Current.AppAudioName;
        _refreshing = true;
        try
        {
            Apps.Clear();
            foreach (var app in apps) Apps.Add(app);
            SelectedApp = Apps.FirstOrDefault(a => a.ExeName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                ?? Apps.FirstOrDefault(a => a.ExeName.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
                ?? Apps.FirstOrDefault(a => a.IsPlaying)
                ?? Apps.FirstOrDefault();
        }
        finally
        {
            _refreshing = false;
        }

        if (!IsStreaming)
            Message = Apps.Count == 0 ? Loc.T("No hay ninguna app con audio. Abre Spotify (o pon algo en el navegador) y pulsa «Actualizar».") : null;
    }

    /// <summary>Lo llama el temporizador de medidores de la ventana (~30 veces por segundo).</summary>
    public void UpdateLevel(float peak)
    {
        double db = peak > 0 ? Math.Max(MeterFloorDb, 20 * Math.Log10(peak)) : MeterFloorDb;
        _levelDb = db >= _levelDb ? db : Math.Max(db, _levelDb - 0.8);
        Level = (_levelDb - MeterFloorDb) / -MeterFloorDb;
    }

    partial void OnSelectedAppChanged(AudioApp? value)
    {
        if (_refreshing || value is null) return;
        _settings.Current.AppAudioName = value.ExeName;
        _settings.ScheduleSave();
        if (IsStreaming && !value.ExeName.Equals(_streamer.Current?.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            _ = StartAsync();
            // Se baja la app nueva y se devuelve su volumen a la anterior.
            if (SongVoiceOn) _ = DuckSelectedAsync(value.ExeName);
        }
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(StreamingName));
        ToggleCommand.NotifyCanExecuteChanged();
        if (!value) _watchTimer.Stop();
        // Sin música no hay cantante al que cambiarle la voz.
        if (!value && SongVoiceOn && !_songVoiceBusy) SongVoiceOn = false;
        if (_syncing) return;
        if (value) _ = StartAsync();
        else _ = StopAsync();
    }

    partial void OnSongVoiceOnChanged(bool value)
    {
        if (_songVoiceBusy) return;
        _ = value ? StartSongVoiceAsync() : StopSongVoiceCoreAsync();
    }

    /// <summary>Apaga «Voz del cantante» (el karaoke lo llama antes de quitar la voz).</summary>
    public async Task StopSongVoiceAsync()
    {
        if (!SongVoiceOn) return;
        _songVoiceBusy = true;
        try
        {
            SongVoiceOn = false;
        }
        finally
        {
            _songVoiceBusy = false;
        }
        await StopSongVoiceCoreAsync();
    }

    /// <summary>Al salir de la app (el volumen de la app lo devuelve <see cref="AppDucking.Shutdown"/>).</summary>
    public void Shutdown()
    {
        _watchTimer.Stop();
    }

    private async Task StartSongVoiceAsync()
    {
        _songVoiceBusy = true;
        try
        {
            if (KaraokeActive)
            {
                Message = Loc.T("Apaga primero el karaoke: quitar la voz y cambiársela al cantante no van a la vez.");
                SongVoiceOn = false;
                return;
            }
            if (SelectedApp is not { } app)
            {
                Message = Loc.T("Elige primero la app que suena.");
                SongVoiceOn = false;
                return;
            }

            ApplySongVoice();
            _streamer.Processing = SongProcessing.ChangeVoice;
            _pipeline.AppAudioToMonitor = true;
            bool wasStreaming = IsStreaming;
            if (!await StreamAppAsync(app.ExeName))
            {
                ResetSongVoiceAudio();
                SongVoiceOn = false;
                return;
            }
            _startedForSongVoice = !wasStreaming;
            await _ducking.DuckAsync(app.ExeName);
            Log.Info($"Voz del cantante: {SongVoice?.Name ?? "normal"} en {app.DisplayName}");
        }
        finally
        {
            _songVoiceBusy = false;
        }
    }

    private async Task StopSongVoiceCoreAsync()
    {
        _songVoiceBusy = true;
        try
        {
            await _ducking.ReleaseAsync();
            ResetSongVoiceAudio();
            if (_startedForSongVoice && IsStreaming) IsStreaming = false;
            _startedForSongVoice = false;
            Log.Info("Voz del cantante: apagada");
        }
        finally
        {
            _songVoiceBusy = false;
        }
    }

    private async Task DuckSelectedAsync(string exeName)
    {
        await _ducking.ReleaseAsync();
        if (SongVoiceOn) await _ducking.DuckAsync(exeName);
    }

    private void ResetSongVoiceAudio()
    {
        _streamer.Processing = SongProcessing.None;
        _pipeline.AppAudioToMonitor = false;
    }

    private void ApplySongVoice(bool liveEdit = false) =>
        _streamer.SongVoice.Voice.SetPreset(SongVoice?.Preset ?? VoicePreset.Neutral, liveEdit);

    private async Task StartAsync()
    {
        if (SelectedApp is not { } selected)
        {
            SetStreaming(false);
            Message = Loc.T("Elige primero la app que quieres transmitir.");
            return;
        }

        // El PID puede haber cambiado (la app se reinició): se busca otra vez por su ejecutable.
        var app = AudioAppFinder.FindByExe(selected.ExeName) ?? selected;
        Message = null;
        IsBusy = true;
        try
        {
            await Task.Run(() => _streamer.Start(app));
            Log.Info($"Música por el micro: {app.DisplayName} (pid {app.ProcessId})");
            if (IsStreaming) _watchTimer.Start();
            else await Task.Run(_streamer.Stop); // la apagaste mientras arrancaba
        }
        catch (AudioDeviceException ex)
        {
            Log.Warn("No se pudo transmitir el audio de la app", ex);
            Message = ex.Message;
            SetStreaming(false);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(StreamingName));
        }
    }

    private async Task StopAsync()
    {
        _watchTimer.Stop();
        Message = null;
        await Task.Run(_streamer.Stop);
        Log.Info("Música por el micro: apagada");
    }

    /// <summary>Si la app se cierra o se reinicia, se vuelve a enganchar sola en cuanto vuelva a estar abierta.</summary>
    private void OnWatchTick()
    {
        if (!IsStreaming || IsBusy) return;
        if (_streamer.Current is { } current && AudioAppFinder.IsAlive(current.ProcessId)) return;
        if (SelectedApp is not { } selected) return;

        if (AudioAppFinder.FindByExe(selected.ExeName) is not null) _ = StartAsync();
        else Message = Loc.F("{0} está cerrada: en cuanto la abras, se volverá a transmitir.", selected.DisplayName);
    }

    private void OnStreamerFaulted(Exception error)
    {
        // La captura ya está parada. Si la app sigue abierta, el siguiente tick la vuelve a arrancar.
        Log.Warn("La música por el micro se ha parado", error);
        Message = error.Message;
    }

    private void SetStreaming(bool value)
    {
        _syncing = true;
        try
        {
            IsStreaming = value;
        }
        finally
        {
            _syncing = false;
        }
    }
}
