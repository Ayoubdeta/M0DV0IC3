using CommunityToolkit.Mvvm.ComponentModel;
using M0DV0IC3.App.Models;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Tarjeta de una voz en la pestaña Voces.</summary>
public sealed partial class VoiceCardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Icon), nameof(Id))]
    private VoicePreset _preset;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _hotkeyHint;

    public VoiceCardViewModel(VoicePreset preset)
    {
        _preset = preset;
        Category = preset.IsBuiltIn ? VoiceCategories.Of(preset.Id) : VoiceCategory.Custom;
    }

    /// <summary>Grupo de la tarjeta: decide su color de acento.</summary>
    public VoiceCategory Category { get; }

    public string Id => Preset.Id;

    public string Name => Preset.Name;

    public string Icon => Preset.Icon;

    public bool IsBuiltIn => Preset.IsBuiltIn;
}
