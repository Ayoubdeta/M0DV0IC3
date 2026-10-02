using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio;
using M0DV0IC3.Audio.AppAudio;
using M0DV0IC3.Dsp;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Pestaña Música: transmitir por el micro lo que suena en una app (Spotify, el navegador...).
/// La app elegida y el volumen se guardan; la transmisión no, para que nunca empiece sola al abrir la app.
/// </summary>
public sealed partial class MusicViewModel : ObservableObject
{
    private const double MeterFloorDb = -60;

    private readonly SettingsService _settings;
    private readonly AppAudioStreamer _streamer;
    private readonly VoicePipeline _pipeline;
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _syncing;
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

    public MusicViewModel(SettingsService settings, AudioService audio)
    {
        _settings = settings;
        _streamer = audio.AppAudio;
        _pipeline = audio.Pipeline;
        _pipeline.AppAudioGain = DspMath.DbToGain(settings.Current.AppAudioVolumeDb);

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

    /// <summary>La app que se transmite ("Spotify"), para la barra de estado; null si no se transmite nada.</summary>
    public string? StreamingName => IsStreaming ? (_streamer.Current ?? SelectedApp)?.DisplayName : null;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private void Toggle() => IsStreaming = !IsStreaming;

    private bool CanToggle() => IsSupported && (SelectedApp is not null || IsStreaming);

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
            Message = Apps.Count == 0 ? "No hay ninguna app con audio. Abre Spotify (o pon algo en el navegador) y pulsa «Actualizar»." : null;
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
        if (IsStreaming && !value.ExeName.Equals(_streamer.Current?.ExeName, StringComparison.OrdinalIgnoreCase)) _ = StartAsync();
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(StreamingName));
        ToggleCommand.NotifyCanExecuteChanged();
        if (!value) _watchTimer.Stop();
        if (_syncing) return;
        if (value) _ = StartAsync();
        else _ = StopAsync();
    }

    private async Task StartAsync()
    {
        if (SelectedApp is not { } selected)
        {
            SetStreaming(false);
            Message = "Elige primero la app que quieres transmitir.";
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
        else Message = $"{selected.DisplayName} está cerrada: en cuanto la abras, se volverá a transmitir.";
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
