using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Una acción con su atajo (fila de la pestaña Atajos, o el atajo de un sonido).</summary>
public sealed partial class HotkeyBindingViewModel : ObservableObject
{
    private readonly HotkeysViewModel _owner;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureText), nameof(HasGesture))]
    private HotkeyGesture? _gesture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureText))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public HotkeyBindingViewModel(HotkeysViewModel owner, string actionId, string label, HotkeyGesture? gesture)
    {
        _owner = owner;
        ActionId = actionId;
        _label = label;
        _gesture = gesture;
    }

    /// <summary>La combinación ha cambiado (para guardarla).</summary>
    public event EventHandler? GestureChanged;

    public string ActionId { get; }

    public bool HasGesture => Gesture is not null;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string GestureText => IsCapturing ? "Pulsa la combinación…" : Gesture?.DisplayText ?? "(sin atajo)";

    partial void OnGestureChanged(HotkeyGesture? value) => GestureChanged?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Change() => _owner.BeginCapture(this);

    [RelayCommand]
    private void Clear() => _owner.ClearGesture(this);
}
