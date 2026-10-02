using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;

namespace M0DV0IC3.App.ViewModels;

/// <summary>
/// Pestaña Atajos: las acciones globales, los atajos de los sonidos y la captura de combinaciones.
/// Mientras se captura, todos los atajos se quitan de Windows para que la combinación llegue a la ventana.
/// </summary>
public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly HotkeyService _service;
    private readonly SettingsService _settings;
    private readonly Action _restartAsAdmin;
    private HotkeyGesture? _beforeCapture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCapturing))]
    private HotkeyBindingViewModel? _capturing;

    public HotkeysViewModel(HotkeyService service, SettingsService settings, Action restartAsAdmin)
    {
        _service = service;
        _settings = settings;
        _restartAsAdmin = restartAsAdmin;

        foreach (var action in HotkeyActions.All)
        {
            string? text = settings.Current.Hotkeys.TryGetValue(action.Id, out string? saved) ? saved : action.DefaultGesture;
            HotkeyGesture? gesture = HotkeyGesture.TryParse(text, out var parsed) ? parsed : null;
            var binding = new HotkeyBindingViewModel(this, action.Id, action.Label, gesture);
            binding.GestureChanged += OnActionGestureChanged;
            Actions.Add(binding);
        }

        SoundBindings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSoundBindings));
    }

    public ObservableCollection<HotkeyBindingViewModel> Actions { get; } = [];

    /// <summary>Atajos de los sonidos; los añade y quita el soundboard.</summary>
    public ObservableCollection<HotkeyBindingViewModel> SoundBindings { get; } = [];

    public bool HasSoundBindings => SoundBindings.Count > 0;

    public bool IsCapturing => Capturing is not null;

    public bool IsElevated => Elevation.IsElevated;

    public string ElevationText => IsElevated
        ? "M0DV0IC3 se está ejecutando como administrador: los atajos también funcionan dentro de juegos abiertos como administrador."
        : "Ahora mismo M0DV0IC3 se está ejecutando sin permisos de administrador.";

    public IEnumerable<HotkeyBindingViewModel> AllBindings => Actions.Concat(SoundBindings);

    public HotkeyBindingViewModel? Find(string actionId) => AllBindings.FirstOrDefault(b => b.ActionId == actionId);

    /// <summary>Registra todos los atajos en Windows y marca los que no se han podido registrar.</summary>
    public void RegisterAll()
    {
        _service.UnregisterAll();
        foreach (var binding in AllBindings) RegisterOne(binding);
    }

    public void UnregisterAll() => _service.UnregisterAll();

    public void AddSoundBinding(HotkeyBindingViewModel binding)
    {
        SoundBindings.Add(binding);
        if (!IsCapturing) RegisterOne(binding);
    }

    public void RemoveSoundBinding(HotkeyBindingViewModel binding)
    {
        if (Capturing == binding) CancelCapture();
        SoundBindings.Remove(binding);
        _service.Unregister(binding.ActionId);
    }

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
            binding.Error = $"{gesture.DisplayText} ya está asignado a «{other.Label}».";
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

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!Dialogs.Confirm("¿Volver a poner los atajos de serie? Los atajos de los sonidos no se tocan.")) return;
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
