using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Un grupo de la pestaña Atajos (Voz, Sonidos...) con sus filas.</summary>
public sealed partial class HotkeyGroupViewModel(HotkeyGroup group, string title, string icon, string? emptyHint = null) : ObservableObject
{
    /// <summary>Se ve: tiene filas que cumplen la búsqueda, o el aviso de que aún no hay ninguna.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>"Añade sonidos en Soundboard..." cuando el grupo solo tiene sus acciones fijas y no se busca nada.</summary>
    [ObservableProperty]
    private bool _showEmptyHint;

    public HotkeyGroup Group { get; } = group;

    public string Title { get; } = title;

    /// <summary>Icono de Segoe MDL2 Assets, el mismo que su pestaña.</summary>
    public string Icon { get; } = icon;

    public string? EmptyHint { get; } = emptyHint;

    public ObservableCollection<HotkeyBindingViewModel> Items { get; } = [];
}

/// <summary>
/// Pestaña Atajos: las acciones globales, los atajos de los sonidos y de las frases, por grupos y con buscador, y la
/// captura de combinaciones. Mientras se captura, todos los atajos se quitan de Windows para que la combinación llegue
/// a la ventana.
/// </summary>
public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly HotkeyService _service;
    private readonly SettingsService _settings;
    private readonly Action _restartAsAdmin;
    private readonly Dictionary<HotkeyGroup, HotkeyGroupViewModel> _groups;
    private HotkeyGesture? _beforeCapture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCapturing))]
    private HotkeyBindingViewModel? _capturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _search = "";

    [ObservableProperty]
    private bool _hasNoResults;

    public HotkeysViewModel(HotkeyService service, SettingsService settings, Action restartAsAdmin)
    {
        _service = service;
        _settings = settings;
        _restartAsAdmin = restartAsAdmin;

        Groups =
        [
            new(HotkeyGroup.Voice, "Voz", ""),
            new(HotkeyGroup.Sounds, "Sonidos", "", "Añade sonidos en la pestaña Soundboard y ponles un atajo desde aquí o desde su engranaje."),
            new(HotkeyGroup.Phrases, "Frases de texto a voz", "", "Guarda frases en la pestaña Texto a voz y ponles un atajo para decirlas en mitad de una partida."),
            new(HotkeyGroup.Mic, "Micro y auriculares", ""),
            new(HotkeyGroup.Music, "Música, karaoke y grabación", ""),
            new(HotkeyGroup.PickVoice, "Elegir una voz", ""),
        ];
        _groups = Groups.ToDictionary(g => g.Group);

        foreach (var action in HotkeyActions.All)
        {
            string? text = settings.Current.Hotkeys.TryGetValue(action.Id, out string? saved) ? saved : action.DefaultGesture;
            HotkeyGesture? gesture = HotkeyGesture.TryParse(text, out var parsed) ? parsed : null;
            var binding = new HotkeyBindingViewModel(this, action.Id, action.Label, gesture, action.Detail);
            binding.GestureChanged += OnActionGestureChanged;
            Actions.Add(binding);
            _groups[action.Group].Items.Add(binding);
        }

        SoundBindings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSoundBindings));
        PhraseBindings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPhraseBindings));
        ApplySearch();
    }

    public ObservableCollection<HotkeyBindingViewModel> Actions { get; } = [];

    /// <summary>Atajos de los sonidos; los añade y quita el soundboard.</summary>
    public ObservableCollection<HotkeyBindingViewModel> SoundBindings { get; } = [];

    public bool HasSoundBindings => SoundBindings.Count > 0;

    /// <summary>Atajos de las frases guardadas de texto a voz; los añade y quita esa pestaña.</summary>
    public ObservableCollection<HotkeyBindingViewModel> PhraseBindings { get; } = [];

    public bool HasPhraseBindings => PhraseBindings.Count > 0;

    public IReadOnlyList<HotkeyGroupViewModel> Groups { get; }

    public bool IsSearching => !string.IsNullOrWhiteSpace(Search);

    public bool IsCapturing => Capturing is not null;

    public bool IsElevated => Elevation.IsElevated;

    public string ElevationText => IsElevated
        ? "M0DV0IC3 se está ejecutando como administrador: los atajos también funcionan dentro de juegos abiertos como administrador."
        : "Si un juego se ejecuta como administrador, abre M0DV0IC3 también como administrador para que los atajos funcionen dentro.";

    public IEnumerable<HotkeyBindingViewModel> AllBindings => Actions.Concat(SoundBindings).Concat(PhraseBindings);

    public HotkeyBindingViewModel? Find(string actionId) => AllBindings.FirstOrDefault(b => b.ActionId == actionId);

    /// <summary>Registra todos los atajos en Windows y marca los que no se han podido registrar.</summary>
    public void RegisterAll()
    {
        _service.UnregisterAll();
        foreach (var binding in AllBindings) RegisterOne(binding);
    }

    public void UnregisterAll() => _service.UnregisterAll();

    public void AddSoundBinding(HotkeyBindingViewModel binding) => AddDynamic(binding, SoundBindings, HotkeyGroup.Sounds);

    public void RemoveSoundBinding(HotkeyBindingViewModel binding) => RemoveDynamic(binding, SoundBindings, HotkeyGroup.Sounds);

    public void AddPhraseBinding(HotkeyBindingViewModel binding) => AddDynamic(binding, PhraseBindings, HotkeyGroup.Phrases);

    public void RemovePhraseBinding(HotkeyBindingViewModel binding) => RemoveDynamic(binding, PhraseBindings, HotkeyGroup.Phrases);

    public void BeginCapture(HotkeyBindingViewModel binding)
    {
        if (Capturing is not null) FinishCapture();
        _service.UnregisterAll();
        _beforeCapture = binding.Gesture;
        binding.Error = null;
        binding.IsCapturing = true;
        Capturing = binding;
    }

    public void CancelCapture() => FinishCapture();

    /// <summary>Tecla pulsada en la ventana durante la captura. Devuelve true si se ha consumido.</summary>
    public bool HandleCaptureKey(Key key, ModifierKeys modifiers)
    {
        var binding = Capturing;
        if (binding is null) return false;

        if (modifiers == ModifierKeys.None && key == Key.Escape)
        {
            FinishCapture();
            return true;
        }
        if (modifiers == ModifierKeys.None && key == Key.Back)
        {
            FinishCapture();
            SetGesture(binding, null);
            return true;
        }
        if (HotkeyGesture.IsModifierKey(key)) return true;
        if (modifiers == ModifierKeys.None && !HotkeyGesture.AllowsNoModifier(key))
        {
            binding.Error = "Añade Ctrl, Alt, Mayús o Win: esa tecla sola dejaría de funcionar en las demás aplicaciones.";
            return true;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        var other = AllBindings.FirstOrDefault(b => b != binding && b.Gesture == gesture);
        FinishCapture();
        if (other is not null)
        {
            binding.Error = $"{gesture.DisplayText} ya lo usa {other.ConflictName}. Quítaselo primero o elige otra combinación.";
            return true;
        }

        SetGesture(binding, gesture);
        if (binding.Error is { } error)
        {
            // Windows no lo deja registrar (lo usa otra app): se vuelve a la combinación anterior.
            SetGesture(binding, _beforeCapture);
            binding.Error = _beforeCapture is null ? error : $"{error} Se mantiene {_beforeCapture.Value.DisplayText}.";
        }
        return true;
    }

    public void ClearGesture(HotkeyBindingViewModel binding)
    {
        if (Capturing == binding) FinishCapture();
        SetGesture(binding, null);
    }

    partial void OnSearchChanged(string value) => ApplySearch();

    [RelayCommand]
    private void ClearSearch() => Search = "";

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!Dialogs.Confirm("¿Volver a poner los atajos de serie? Los atajos de los sonidos y de las frases no se tocan.")) return;
        FinishCapture();
        foreach (var binding in Actions)
        {
            string? text = HotkeyActions.All.First(a => a.Id == binding.ActionId).DefaultGesture;
            binding.Gesture = HotkeyGesture.TryParse(text, out var parsed) ? parsed : null;
        }
        RegisterAll();
    }

    [RelayCommand]
    private void RestartAsAdmin() => _restartAsAdmin();

    private void AddDynamic(HotkeyBindingViewModel binding, ObservableCollection<HotkeyBindingViewModel> list, HotkeyGroup group)
    {
        list.Add(binding);
        _groups[group].Items.Add(binding);
        binding.PropertyChanged += OnDynamicBindingChanged;
        if (!IsCapturing) RegisterOne(binding);
        ApplySearch();
    }

    private void RemoveDynamic(HotkeyBindingViewModel binding, ObservableCollection<HotkeyBindingViewModel> list, HotkeyGroup group)
    {
        if (Capturing == binding) CancelCapture();
        list.Remove(binding);
        _groups[group].Items.Remove(binding);
        binding.PropertyChanged -= OnDynamicBindingChanged;
        _service.Unregister(binding.ActionId);
        ApplySearch();
    }

    // Un sonido renombrado puede dejar de cumplir la búsqueda, o empezar a cumplirla.
    private void OnDynamicBindingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotkeyBindingViewModel.Label) && IsSearching) ApplySearch();
    }

    /// <summary>Muestra solo las filas que cumplen la búsqueda, y oculta los grupos que se quedan vacíos.</summary>
    private void ApplySearch()
    {
        string query = HotkeyBindingViewModel.Normalize(Search ?? "");
        bool any = false;
        foreach (var group in Groups)
        {
            bool visible = false;
            foreach (var binding in group.Items)
            {
                binding.IsVisible = binding.Matches(query);
                visible |= binding.IsVisible;
            }
            bool onlyFixed = group.Group switch
            {
                HotkeyGroup.Sounds => !HasSoundBindings,
                HotkeyGroup.Phrases => !HasPhraseBindings,
                _ => false,
            };
            group.ShowEmptyHint = query.Length == 0 && onlyFixed;
            group.IsVisible = visible || group.ShowEmptyHint;
            any |= visible;
        }
        HasNoResults = !any;
    }

    private void FinishCapture()
    {
        var binding = Capturing;
        if (binding is null) return;
        binding.IsCapturing = false;
        Capturing = null;
        RegisterAll();
    }

    private void SetGesture(HotkeyBindingViewModel binding, HotkeyGesture? gesture)
    {
        binding.Gesture = gesture;
        RegisterOne(binding);
    }

    private void RegisterOne(HotkeyBindingViewModel binding)
    {
        if (binding.Gesture is { } gesture)
        {
            binding.Error = _service.Register(binding.ActionId, gesture, out string? error) ? null : error;
        }
        else
        {
            _service.Unregister(binding.ActionId);
            binding.Error = null;
        }
    }

    private void OnActionGestureChanged(object? sender, EventArgs e)
    {
        if (sender is not HotkeyBindingViewModel binding) return;
        _settings.Current.Hotkeys[binding.ActionId] = binding.Gesture?.ToString() ?? "";
        _settings.ScheduleSave();
    }
}
