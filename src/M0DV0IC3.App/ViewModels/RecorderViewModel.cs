using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Localization;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.Recording;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Botón «Grabar» de la barra inferior: graba lo que sale por el micro (tu voz con su efecto, los sonidos, las frases
/// y la música del karaoke) y lo deja en MP3 en Música\M0DV0IC3, listo para compartir o para el soundboard.
/// </summary>
public sealed partial class RecorderViewModel : ObservableObject
{
    private readonly MicRecorder _recorder;
    private readonly SoundboardViewModel _soundboard;
    private readonly DispatcherTimer _timer;
    private string? _wavPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(CanToggle))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private string _elapsedText = "0:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    private bool _engineRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedName), nameof(ShowResult))]
    private string? _savedPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    private string? _error;

    public RecorderViewModel(MicRecorder recorder, SoundboardViewModel soundboard)
    {
        _recorder = recorder;
        _soundboard = soundboard;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
    }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "M0DV0IC3");

    /// <summary>El interruptor de la barra: encenderlo empieza a grabar y apagarlo guarda.</summary>
    public bool Recording
    {
        get => IsRecording;
        set
        {
            if (value == IsRecording) return;
            if (value) Start();
            else _ = StopAsync();
            OnPropertyChanged();
        }
    }

    public string ButtonText => IsRecording ? Loc.F("Grabando {0}", ElapsedText) : Loc.T("Grabar");

    /// <summary>Se puede empezar con el audio en marcha, y parar siempre.</summary>
    public bool CanToggle => IsRecording || EngineRunning;

    public string? SavedName => SavedPath is null ? null : Path.GetFileName(SavedPath);

    public bool ShowResult => SavedPath is not null || IsSaving || Error is not null;

    /// <summary>Atajo: empieza o para.</summary>
    public void Toggle()
    {
        if (CanToggle) Recording = !Recording;
    }

    /// <summary>Al salir de la app: se cierra el WAV (no da tiempo a pasarlo a MP3).</summary>
    public void Shutdown()
    {
        _timer.Stop();
        _recorder.Stop();
    }

    private void Start()
    {
        Error = null;
        SavedPath = null;
        string path = Path.Combine(Folder, $"M0DV0IC3 {DateTime.Now:yyyy-MM-dd HH.mm.ss}.wav");
        try
        {
            _recorder.Start(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn($"No se pudo empezar a grabar en {path}", ex);
            Error = Loc.F("No se pudo empezar a grabar: {0}", ex.Message);
            OnPropertyChanged(nameof(Recording));
            return;
        }
        _wavPath = path;
        ElapsedText = "0:00";
        IsRecording = true;
        _timer.Start();
        Log.Info($"Grabando en {path}");
    }

    private async Task StopAsync()
    {
        _timer.Stop();
        var length = _recorder.Stop();
        IsRecording = false;
        OnPropertyChanged(nameof(Recording));
        string? wav = _wavPath;
        _wavPath = null;
        if (wav is null) return;

        if (_recorder.Failure is { } failure) Error = Loc.F("La grabación se cortó: {0}", failure);
        if (length < TimeSpan.FromSeconds(0.3))
        {
            TryDelete(wav);
            Error ??= Loc.T("No se ha grabado nada: ¿está el audio en marcha?");
            return;
        }
        if (_recorder.SamplesDropped > 0) Log.Warn($"La grabación perdió {_recorder.SamplesDropped} muestras (disco lento)");

        IsSaving = true;
        try
        {
            SavedPath = await Task.Run(() => MicRecorder.ConvertToMp3(wav));
            Log.Info($"Grabación guardada: {SavedPath} ({length.TotalSeconds:0.0} s)");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = _recorder.Elapsed;
        ElapsedText = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
        // Se ha cortado sola (disco lleno...).
        if (!_recorder.IsRecording && IsRecording) _ = StopAsync();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            if (SavedPath is { } path && File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn("No se pudo abrir la carpeta de grabaciones", ex);
        }
    }

    [RelayCommand]
    private async Task AddToSoundboard()
    {
        if (SavedPath is not { } path) return;
        await _soundboard.AddFilesAsync([path]);
        Dismiss();
    }

    [RelayCommand]
    private void Dismiss()
    {
        SavedPath = null;
        Error = null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se pudo borrar {path}", ex);
        }
    }
}
