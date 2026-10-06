using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Una acción con su atajo (fila de la pestaña Atajos, o el atajo de un sonido o de una frase).</summary>
public sealed partial class HotkeyBindingViewModel : ObservableObject
{
    private readonly HotkeysViewModel _owner;

    [ObservableProperty]
    private string _label;

    /// <summary>Segunda línea de la fila: qué hace.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string? _detail;

    /// <summary>La voz a la que lleva (Voz 1…9 y mantener pulsado), con su icono.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string? _voiceName;

    [ObservableProperty]
    private string? _voiceIcon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureText), nameof(HasGesture), nameof(KeyParts))]
    private HotkeyGesture? _gesture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureText))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>Cumple la búsqueda de la pestaña Atajos.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    public HotkeyBindingViewModel(HotkeysViewModel owner, string actionId, string label, HotkeyGesture? gesture, string? detail = null)
    {
        _owner = owner;
        ActionId = actionId;
        _label = label;
        _gesture = gesture;
        _detail = detail;
    }

    /// <summary>La combinación ha cambiado (para guardarla).</summary>
    public event EventHandler? GestureChanged;

    public string ActionId { get; }

    public bool HasGesture => Gesture is not null;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasDetail => Detail is not null || VoiceName is not null;

    public string GestureText => IsCapturing ? "Pulsa la combinación…" : Gesture?.DisplayText ?? "(sin atajo)";

    /// <summary>Las teclas de la combinación, para dibujarlas una a una.</summary>
    public IReadOnlyList<string> KeyParts => Gesture?.DisplayParts ?? [];

    /// <summary>Cómo se nombra en el aviso de "ya está asignado a…".</summary>
    public string ConflictName =>
        ActionId.StartsWith(HotkeyActions.SoundPrefix, StringComparison.Ordinal) ? $"el sonido «{Label}»"
        : ActionId.StartsWith(HotkeyActions.PhrasePrefix, StringComparison.Ordinal) ? $"la frase «{Label}»"
        : $"«{Label}»";

    /// <summary>¿Aparece al buscar <paramref name="query"/> (ya pasado por <see cref="Normalize"/>)?</summary>
    public bool Matches(string query) =>
        query.Length == 0
        || Normalize(Label).Contains(query, StringComparison.Ordinal)
        || (Detail is not null && Normalize(Detail).Contains(query, StringComparison.Ordinal))
        || (VoiceName is not null && Normalize(VoiceName).Contains(query, StringComparison.Ordinal))
        || (Gesture is { } gesture && Normalize(gesture.DisplayText).Contains(query, StringComparison.Ordinal));

    /// <summary>Minúsculas y sin tildes: "música" encuentra "Musica".</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    partial void OnGestureChanged(HotkeyGesture? value) => GestureChanged?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Change() => _owner.BeginCapture(this);

    [RelayCommand]
    private void Clear() => _owner.ClearGesture(this);
}
