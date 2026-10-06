using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.Soundboard;
using Microsoft.Win32;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Pestaña Soundboard. Los archivos se copian a %AppData%\M0DV0IC3\sounds y se decodifican en segundo plano;
/// los clips quedan en memoria para que sonar no toque el disco.
/// </summary>
public sealed partial class SoundboardViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly SoundboardMixer _mixer;
    private readonly HotkeysViewModel _hotkeys;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SoundItemViewModel? _selected;

    public SoundboardViewModel(SettingsService settings, SoundboardMixer mixer, HotkeysViewModel hotkeys)
    {
        _settings = settings;
        _mixer = mixer;
        _hotkeys = hotkeys;
        _mixer.MasterVolume = (float)settings.Current.SoundboardVolume;

        // Los elementos se crean ya (para que sus atajos se registren); los clips se cargan en LoadSavedSounds.
        foreach (var entry in settings.Current.Sounds) Sounds.Add(CreateItem(entry));
        Sounds.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSounds));
    }

    public ObservableCollection<SoundItemViewModel> Sounds { get; } = [];

    public bool HasSounds => Sounds.Count > 0;

    public bool HasSelection => Selected is not null;

    public string SupportedFormatsText => string.Join(", ", SoundLoader.SupportedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));

    /// <summary>Volumen general (0..2).</summary>
    public double MasterVolume
    {
        get => _settings.Current.SoundboardVolume;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0, 2);
            if (value == _settings.Current.SoundboardVolume) return;
            _settings.Current.SoundboardVolume = value;
            _mixer.MasterVolume = (float)value;
            OnPropertyChanged();
            _settings.ScheduleSave();
        }
    }

    /// <summary>Decodifica en segundo plano los sonidos guardados.</summary>
    public async Task LoadSavedSoundsAsync()
    {
        foreach (var item in Sounds.ToList())
        {
            if (item.Clip is null) await LoadClipAsync(item);
        }
    }

    public void PlayById(string soundId)
    {
        var item = Sounds.FirstOrDefault(s => s.Id == soundId);
        if (item is not null) Play(item);
    }

    public void Play(SoundItemViewModel item)
    {
        item.FlushTrim();
        if (item.Clip is { } clip) _mixer.Play(clip, (float)item.Volume);
    }

    public void Stop(SoundItemViewModel item)
    {
        if (item.Clip is { } clip) _mixer.Stop(clip);
    }

    [RelayCommand]
    public void StopAll() => _mixer.StopAll();

    public void Delete(SoundItemViewModel item)
    {
        if (!Dialogs.Confirm($"¿Quitar el sonido «{item.Name}» del soundboard?")) return;
        Stop(item);
        RemoveItem(item, deleteFile: true);
        _settings.Current.Sounds.Remove(item.Entry);
        _settings.ScheduleSave();
    }

    public void OnItemChanged() => _settings.ScheduleSave();

    [RelayCommand]
    private async Task AddSound()
    {
        string patterns = string.Join(";", SoundLoader.SupportedExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = "Añadir sonidos al soundboard",
            Filter = $"Archivos de audio ({patterns})|{patterns}|Todos los archivos (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() == true) await AddFilesAsync(dialog.FileNames);
    }

    [RelayCommand]
    private void CloseDetails() => Selected = null;

    /// <summary>Añade archivos (botón o arrastrar y soltar).</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var failed = new List<string>();
        foreach (string source in paths)
        {
            string fileName = Path.GetFileName(source);
            if (!File.Exists(source) || !SoundLoader.IsSupported(source))
            {
                failed.Add($"{fileName} (formato no admitido)");
                continue;
            }

            var entry = new SoundEntry
            {
                Name = Path.GetFileNameWithoutExtension(source),
                FileName = $"{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}",
            };
            var item = CreateItem(entry);
            item.IsLoading = true;
            Sounds.Add(item);

            string destination = Path.Combine(AppPaths.SoundsFolder, entry.FileName);
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(AppPaths.SoundsFolder);
                    File.Copy(source, destination, overwrite: false);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"No se pudo copiar {source}", ex);
                RemoveItem(item, deleteFile: false);
                failed.Add($"{fileName} (no se pudo copiar: {ex.Message})");
                continue;
            }

            if (await LoadClipAsync(item))
            {
                _settings.Current.Sounds.Add(entry);
                _settings.ScheduleSave();
            }
            else
            {
                RemoveItem(item, deleteFile: true);
                failed.Add($"{fileName} (no se pudo leer el audio)");
            }
        }

        if (failed.Count > 0)
        {
            Dialogs.Warning("No se han podido añadir estos archivos:\n\n• " + string.Join("\n• ", failed)
                + $"\n\nFormatos admitidos: {SupportedFormatsText}.");
        }
    }

    private SoundItemViewModel CreateItem(SoundEntry entry)
    {
        HotkeyGesture? gesture = HotkeyGesture.TryParse(entry.Hotkey, out var parsed) ? parsed : null;
        var binding = new HotkeyBindingViewModel(_hotkeys, HotkeyActions.SoundAction(entry.Id), $"Sonido: {entry.Name}", gesture);
        binding.GestureChanged += (_, _) =>
        {
            entry.Hotkey = binding.Gesture?.ToString();
            _settings.ScheduleSave();
        };
        _hotkeys.AddSoundBinding(binding);
        return new SoundItemViewModel(this, entry, binding);
    }

    private void RemoveItem(SoundItemViewModel item, bool deleteFile)
    {
        if (Selected == item) Selected = null;
        _hotkeys.RemoveSoundBinding(item.Hotkey);
        Sounds.Remove(item);
        if (!deleteFile) return;
        try
        {
            File.Delete(Path.Combine(AppPaths.SoundsFolder, item.Entry.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se pudo borrar {item.Entry.FileName}", ex);
        }
    }

    private async Task<bool> LoadClipAsync(SoundItemViewModel item)
    {
        string path = Path.Combine(AppPaths.SoundsFolder, item.Entry.FileName);
        item.IsLoading = true;
        item.LoadError = null;
        if (!File.Exists(path))
        {
            Log.Warn($"Falta el archivo del sonido «{item.Name}»: {path}");
            item.LoadError = "Falta el archivo";
            item.IsLoading = false;
            return false;
        }
        try
        {
            string id = item.Id;
            string name = item.Name;
            item.SetFullClip(await Task.Run(() => SoundLoader.Load(path, id, name)));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo cargar el sonido {path}", ex);
            item.LoadError = "No se pudo leer el audio";
            return false;
        }
        finally
        {
            item.IsLoading = false;
        }
    }
}
