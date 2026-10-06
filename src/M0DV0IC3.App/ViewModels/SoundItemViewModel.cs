using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.Soundboard;
using M0DV0IC3.Dsp;
using Microsoft.Win32;
using NAudio.Wave;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Un sonido del soundboard: botón, volumen, atajo, recorte y el clip ya decodificado en memoria. Se guarda el sonido
/// entero (<see cref="FullClip"/>) y el trozo que suena (<see cref="Clip"/>), así el recorte se puede cambiar o
/// deshacer sin volver a leer el archivo.
/// </summary>
public sealed partial class SoundItemViewModel : ObservableObject
{
    private const int WaveformBars = 240;

    private readonly SoundboardViewModel _owner;
    private DispatcherTimer? _trimTimer;
    private bool _loadingTrim;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _name;

    [ObservableProperty]
    private double _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsReady))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasError))]
    private string? _loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsReady), nameof(DurationText))]
    private SoundClip? _clip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullDuration), nameof(HasFullClip))]
    private SoundClip? _fullClip;

    [ObservableProperty]
    private float[]? _peaks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimText), nameof(IsTrimmed))]
    private double _trimStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimText), nameof(IsTrimmed))]
    private double _trimEnd;

    public SoundItemViewModel(SoundboardViewModel owner, SoundEntry entry, HotkeyBindingViewModel hotkey)
    {
        _owner = owner;
        Entry = entry;
        Hotkey = hotkey;
        _name = entry.Name;
        _volume = entry.Volume;
        hotkey.PropertyChanged += OnHotkeyPropertyChanged;
    }

    public SoundEntry Entry { get; }

    public string Id => Entry.Id;

    public HotkeyBindingViewModel Hotkey { get; }

    public bool IsReady => Clip is not null && !IsLoading;

    public bool HasError => LoadError is not null;

    public string DurationText => Clip is null ? "" : $"{Clip.Duration.TotalSeconds:0.0} s";

    public bool HasFullClip => FullClip is not null;

    public double FullDuration => FullClip?.Duration.TotalSeconds ?? 0;

    public bool IsTrimmed => FullClip is not null && (TrimStart > 0.005 || TrimEnd < FullDuration - 0.005);

    public string TrimText => FullClip is null ? ""
        : $"Suena de {TrimStart:0.00} s a {TrimEnd:0.00} s: {TrimEnd - TrimStart:0.00} s de {FullDuration:0.00} s";

    /// <summary>Recibe el sonido entero recién leído y aplica el recorte guardado.</summary>
    public void SetFullClip(SoundClip full)
    {
        _loadingTrim = true;
        try
        {
            FullClip = full;
            Peaks = SoundTrim.Peaks(full.Samples, WaveformBars);
            double duration = full.Duration.TotalSeconds;
            TrimEnd = Entry.TrimEndSeconds > 0 ? Math.Min(Entry.TrimEndSeconds, duration) : duration;
            TrimStart = Math.Clamp(Entry.TrimStartSeconds, 0, Math.Max(0, TrimEnd - SoundTrim.MinimumSeconds));
        }
        finally
        {
            _loadingTrim = false;
        }
        Clip = SoundTrim.Apply(full, TrimStart, TrimEnd);
    }

    // Inicio y fin no se cruzan: siempre queda al menos SoundTrim.MinimumSeconds de sonido.
    partial void OnTrimStartChanged(double value)
    {
        if (!_loadingTrim && FullClip is not null && value > TrimEnd - SoundTrim.MinimumSeconds)
        {
            TrimStart = Math.Max(0, TrimEnd - SoundTrim.MinimumSeconds);
            return;
        }
        OnTrimChanged();
    }

    partial void OnTrimEndChanged(double value)
    {
        if (!_loadingTrim && FullClip is not null && value < TrimStart + SoundTrim.MinimumSeconds)
        {
            TrimEnd = Math.Min(FullDuration, TrimStart + SoundTrim.MinimumSeconds);
            return;
        }
        OnTrimChanged();
    }

    /// <summary>
    /// Guarda el recorte enseguida y rehace el trozo un momento después de soltar, para no copiar el sonido entero
    /// en cada movimiento del ratón.
    /// </summary>
    private void OnTrimChanged()
    {
        if (_loadingTrim || FullClip is null) return;
        Entry.TrimStartSeconds = Math.Round(TrimStart, 3);
        Entry.TrimEndSeconds = TrimEnd >= FullDuration - 0.001 ? 0 : Math.Round(TrimEnd, 3);
        _owner.OnItemChanged();
        if (_trimTimer is null)
        {
            _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _trimTimer.Tick += (_, _) =>
            {
                _trimTimer.Stop();
                ApplyTrim();
            };
        }
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    private void ApplyTrim()
    {
        if (FullClip is null) return;
        _owner.Stop(this);
        Clip = SoundTrim.Apply(FullClip, TrimStart, TrimEnd);
    }

    /// <summary>Aplica ya el recorte pendiente (antes de reproducir).</summary>
    public void FlushTrim()
    {
        if (_trimTimer is not { IsEnabled: true }) return;
        _trimTimer.Stop();
        ApplyTrim();
    }

    [RelayCommand]
    private void ResetTrim()
    {
        if (FullClip is null) return;
        TrimStart = 0;
        TrimEnd = FullDuration;
    }

    /// <summary>Quita los silencios del principio y del final.</summary>
    [RelayCommand]
    private void TrimSilence()
    {
        if (FullClip is null) return;
        var (start, end) = SoundTrim.DetectSound(FullClip.Samples);
        TrimEnd = Math.Round(end, 2);
        TrimStart = Math.Round(start, 2);
    }

    /// <summary>Guarda el trozo recortado como un archivo WAV nuevo (el original no se toca).</summary>
    [RelayCommand]
    private void ExportWav()
    {
        FlushTrim();
        if (Clip is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Guardar el sonido recortado",
            Filter = "Audio WAV (*.wav)|*.wav",
            FileName = string.Concat(Name.Split(Path.GetInvalidFileNameChars())) + ".wav",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var writer = new WaveFileWriter(dialog.FileName, new WaveFormat(DspMath.SampleRate, 16, 1));
            writer.WriteSamples(Clip.Samples, 0, Clip.Samples.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"No se pudo guardar el archivo:\n{ex.Message}");
        }
    }

    /// <summary>Segunda línea del botón: cargando, error, atajo o duración.</summary>
    public string StatusText =>
        IsLoading ? "Cargando…"
        : LoadError ?? (Hotkey.HasGesture ? Hotkey.GestureText : DurationText);

    partial void OnNameChanged(string value)
    {
        Entry.Name = value;
        Hotkey.Label = $"Sonido: {value}";
        _owner.OnItemChanged();
    }

    partial void OnVolumeChanged(double value)
    {
        Entry.Volume = Math.Round(value, 2);
        _owner.OnItemChanged();
    }

    [RelayCommand]
    private void Play() => _owner.Play(this);

    [RelayCommand]
    private void Stop() => _owner.Stop(this);

    [RelayCommand]
    private void Edit() => _owner.Selected = this;

    [RelayCommand]
    private void Delete() => _owner.Delete(this);

    private void OnHotkeyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HotkeyBindingViewModel.Gesture) or nameof(HotkeyBindingViewModel.GestureText))
            OnPropertyChanged(nameof(StatusText));
    }
}
