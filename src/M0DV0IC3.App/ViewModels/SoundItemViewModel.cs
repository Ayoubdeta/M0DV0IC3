using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.Audio.Soundboard;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Un sonido del soundboard: botón, volumen, atajo y el clip ya decodificado en memoria.</summary>
public sealed partial class SoundItemViewModel : ObservableObject
{
    private readonly SoundboardViewModel _owner;

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
