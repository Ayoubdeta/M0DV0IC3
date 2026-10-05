using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.Soundboard;
using Microsoft.Win32;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Pestaña Soundboard. Los archivos se copian a %AppData%\M0DV0IC3\sounds y se decodifican en segundo plano;
/// los clips quedan en memoria para que sonar no toque el disco. Los sonidos incluidos (<see cref="BuiltInSounds"/>)
/// se añaden una vez y se leen de la carpeta de la app.
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
        if (settings.Current.BuiltInSoundsVersion < BuiltInSounds.Version)
        {
            AddBuiltIns(BuiltInSounds.All.Where(s => s.Since > settings.Current.BuiltInSoundsVersion));
            settings.Current.BuiltInSoundsVersion = BuiltInSounds.Version;
            settings.ScheduleSave();
        }
        Sounds.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSounds));

        var view = new ListCollectionView(Sounds);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SoundItemViewModel.Group)) { CustomSort = new GroupOrder() });
        SoundsView = view;
    }

    public ObservableCollection<SoundItemViewModel> Sounds { get; } = [];

    /// <summary>Los sonidos agrupados: los del usuario primero y luego los incluidos, por grupo.</summary>
    public ICollectionView SoundsView { get; }

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
        string message = item.IsBuiltIn
            ? $"¿Quitar «{item.Name}» del soundboard?\n\nEs un sonido incluido: lo puedes recuperar con «Restaurar incluidos»."
            : $"¿Quitar el sonido «{item.Name}» del soundboard?";
        if (!Dialogs.Confirm(message)) return;
        Stop(item);
        RemoveItem(item, deleteFile: !item.IsBuiltIn);
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

    /// <summary>Vuelve a añadir los sonidos incluidos que se hayan quitado.</summary>
    [RelayCommand]
    private async Task RestoreBuiltInSounds()
    {
        var present = Sounds.Select(s => s.Entry.BuiltIn).ToHashSet();
        var missing = BuiltInSounds.All.Where(s => !present.Contains(s.Path)).ToList();
        if (missing.Count == 0)
        {
            Dialogs.Info("Ya tienes todos los sonidos incluidos.");
            return;
        }
        var added = AddBuiltIns(missing);
        _settings.ScheduleSave();
        foreach (var item in added) await LoadClipAsync(item);
    }

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
            // Los del usuario van antes que los incluidos, también en settings.json.
            int index = Sounds.TakeWhile(s => !s.IsBuiltIn).Count();
            Sounds.Insert(index, item);

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
                _settings.Current.Sounds.Insert(_settings.Current.Sounds.TakeWhile(e => e.BuiltIn is null).Count(), entry);
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

    private List<SoundItemViewModel> AddBuiltIns(IEnumerable<BuiltInSound> sounds)
    {
        var added = new List<SoundItemViewModel>();
        foreach (var sound in sounds)
        {
            var entry = BuiltInSounds.CreateEntry(sound);
            _settings.Current.Sounds.Add(entry);
            var item = CreateItem(entry);
            Sounds.Add(item);
            added.Add(item);
        }
        return added;
    }

    private static string PathOf(SoundEntry entry) =>
        BuiltInSounds.Find(entry.BuiltIn) is { } builtIn ? BuiltInSounds.FullPath(builtIn) : Path.Combine(AppPaths.SoundsFolder, entry.FileName);

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
        string path = PathOf(item.Entry);
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
            item.Clip = await Task.Run(() => SoundLoader.Load(path, id, name));
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

    /// <summary>Ordena los grupos por <see cref="SoundGroup.Order"/>, no por orden de aparición.</summary>
    private sealed class GroupOrder : IComparer
    {
        public int Compare(object? x, object? y) => Order(x).CompareTo(Order(y));

        private static int Order(object? group) => (group as CollectionViewGroup)?.Name is SoundGroup g ? g.Order : int.MaxValue;
    }
}
