using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Opción de un desplegable: el valor y su texto.</summary>
public sealed record EditorOption<T>(T Value, string Name);

/// <summary>
/// Editor de una voz personalizada. Cada cambio construye un preset nuevo (con <c>with</c>) y lo
/// entrega enseguida, para que se oiga en directo y se guarde solo.
/// </summary>
public sealed partial class VoiceEditorViewModel : ObservableObject
{
    private static readonly IReadOnlyList<EditorOption<AutotuneScale>> ScaleOptions =
    [
        new(AutotuneScale.Off, "Apagado"),
        new(AutotuneScale.Chromatic, "Cromática (todas las notas)"),
        new(AutotuneScale.Major, "Mayor"),
        new(AutotuneScale.Minor, "Menor"),
        new(AutotuneScale.HarmonicMinor, "Menor armónica"),
        new(AutotuneScale.MinorPentatonic, "Pentatónica menor"),
    ];

    private const string DefaultIcon = "🎭";

    private readonly VoicePreset _original;
    private readonly Action<VoicePreset> _changed;
    private readonly Action _close;
    private VoicePreset _current;
    private bool _loading;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _icon = DefaultIcon;
    [ObservableProperty] private double _pitch;
    [ObservableProperty] private double _formant = 1;
    [ObservableProperty] private bool _robotEnabled;
    [ObservableProperty] private double _robotHz = 110;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutotuneEnabled), nameof(AutotuneKeyEnabled))]
    private AutotuneScale _autotuneScale;

    [ObservableProperty] private int _autotuneKey = 9;
    [ObservableProperty] private double _autotuneRetuneMs;
    [ObservableProperty] private double _autotuneExaggeration;
    [ObservableProperty] private double _vibratoSemitones;
    [ObservableProperty] private double _vibratoHz = 5.5;
    [ObservableProperty] private bool _reverseEnabled;
    [ObservableProperty] private double _reverseMs = 200;
    [ObservableProperty] private double _whisperMix;
    [ObservableProperty] private double _breathiness;
    [ObservableProperty] private double _ringHz = 60;
    [ObservableProperty] private double _ringMix;
    [ObservableProperty] private double _distortion;
    [ObservableProperty] private double _highPassHz;
    [ObservableProperty] private bool _lowPassEnabled;
    [ObservableProperty] private double _lowPassHz = 6000;
    [ObservableProperty] private double _presenceDb;
    [ObservableProperty] private double _highShelfDb;
    [ObservableProperty] private double _chorusMix;
    [ObservableProperty] private double _flangerMix;
    [ObservableProperty] private double _flangerHz = 0.25;
    [ObservableProperty] private double _echoMs = 250;
    [ObservableProperty] private double _echoFeedback = 0.3;
    [ObservableProperty] private double _echoMix;
    [ObservableProperty] private double _reverbMix;
    [ObservableProperty] private double _reverbSize = 0.5;
    [ObservableProperty] private double _outputGainDb;

    public VoiceEditorViewModel(VoiceCardViewModel card, Action<VoicePreset> changed, Action close)
    {
        Card = card;
        _original = _current = card.Preset;
        _changed = changed;
        _close = close;
        Load(card.Preset);
    }

    public VoiceCardViewModel Card { get; }

    public IReadOnlyList<EditorOption<AutotuneScale>> Scales => ScaleOptions;

    public IReadOnlyList<string> Keys => AutotuneNames.Notes;

    public bool AutotuneEnabled => AutotuneScale != AutotuneScale.Off;

    /// <summary>La escala cromática tiene todas las notas: no hay tónica que elegir.</summary>
    public bool AutotuneKeyEnabled => AutotuneScale is not (AutotuneScale.Off or AutotuneScale.Chromatic);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading) Commit();
    }

    // Robot y autotune deciden los dos el tono de salida: se activa solo uno.
    partial void OnRobotEnabledChanged(bool value)
    {
        if (value && !_loading) AutotuneScale = AutotuneScale.Off;
    }

    partial void OnAutotuneScaleChanged(AutotuneScale value)
    {
        if (value != AutotuneScale.Off && !_loading) RobotEnabled = false;
    }

    [RelayCommand]
    private void Undo()
    {
        Load(_original);
        Commit();
    }

    /// <summary>Deja la voz sin efectos, conservando nombre e icono.</summary>
    [RelayCommand]
    private void ClearEffects()
    {
        Load(VoicePreset.Neutral with { Id = _current.Id, Name = _current.Name, Icon = _current.Icon, IsBuiltIn = false });
        Commit();
    }

    [RelayCommand]
    private void Close() => _close();

    private void Load(VoicePreset p)
    {
        _loading = true;
        try
        {
            Name = p.Name;
            Icon = p.Icon;
            Pitch = p.PitchSemitones;
            Formant = p.FormantRatio;
            RobotEnabled = p.RobotHz > 0;
            if (p.RobotHz > 0) RobotHz = Math.Clamp(p.RobotHz, 60, 300);
            AutotuneScale = p.AutotuneScale;
            AutotuneKey = ((p.AutotuneKey % 12) + 12) % 12;
            AutotuneRetuneMs = Math.Clamp(p.AutotuneRetuneMs, 0, 200);
            AutotuneExaggeration = Math.Clamp(p.AutotuneExaggeration, 0, 1);
            VibratoSemitones = p.VibratoSemitones;
            VibratoHz = p.VibratoHz > 0 ? Math.Clamp(p.VibratoHz, 0.2, 10) : 5.5;
            ReverseEnabled = p.ReverseMs > 0;
            if (p.ReverseMs > 0) ReverseMs = Math.Clamp(p.ReverseMs, 60, 500);
            WhisperMix = p.WhisperMix;
            Breathiness = p.Breathiness;
            RingHz = p.RingModHz > 0 ? Math.Clamp(p.RingModHz, 20, 1000) : 60;
            RingMix = p.RingModMix;
            Distortion = p.Distortion;
            HighPassHz = p.HighPassHz;
            LowPassEnabled = p.LowPassHz > 0;
            if (p.LowPassHz > 0) LowPassHz = Math.Clamp(p.LowPassHz, 1000, 12000);
            PresenceDb = p.PresenceDb;
            HighShelfDb = p.HighShelfDb;
            ChorusMix = p.ChorusMix;
            FlangerMix = p.FlangerMix;
            FlangerHz = Math.Clamp(p.FlangerHz, 0.05, 2);
            EchoMs = p.EchoMs > 0 ? p.EchoMs : 250;
            EchoFeedback = p.EchoFeedback;
            EchoMix = p.EchoMix;
            ReverbMix = p.ReverbMix;
            ReverbSize = p.ReverbSize;
            OutputGainDb = p.OutputGainDb;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Commit()
    {
        // Se redondea al paso de cada slider: así un 0,0001 no activa PSOLA ni cambia la latencia.
        var preset = _current with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Sin nombre" : Name.Trim(),
            Icon = string.IsNullOrWhiteSpace(Icon) ? DefaultIcon : Icon.Trim(),
            IsBuiltIn = false,
            PitchSemitones = Math.Round(Pitch * 2) / 2,
            FormantRatio = Math.Round(Formant, 2),
            RobotHz = RobotEnabled ? Math.Round(RobotHz) : 0,
            AutotuneScale = AutotuneScale,
            AutotuneKey = AutotuneKey,
            AutotuneRetuneMs = Math.Round(AutotuneRetuneMs),
            AutotuneExaggeration = Math.Round(AutotuneExaggeration, 2),
            VibratoSemitones = Math.Round(VibratoSemitones, 1),
            VibratoHz = Math.Round(VibratoHz, 2),
            ReverseMs = ReverseEnabled ? Math.Round(ReverseMs / 10) * 10 : 0,
            WhisperMix = Math.Round(WhisperMix, 2),
            Breathiness = Math.Round(Breathiness, 2),
            RingModHz = Math.Round(RingHz),
            RingModMix = Math.Round(RingMix, 2),
            Distortion = Math.Round(Distortion, 2),
            HighPassHz = HighPassHz < 20 ? 0 : Math.Round(HighPassHz),
            LowPassHz = LowPassEnabled ? Math.Round(LowPassHz) : 0,
            PresenceDb = Math.Round(PresenceDb * 2) / 2,
            HighShelfDb = Math.Round(HighShelfDb * 2) / 2,
            ChorusMix = Math.Round(ChorusMix, 2),
            FlangerMix = Math.Round(FlangerMix, 2),
            FlangerHz = Math.Round(FlangerHz, 2),
            EchoMs = Math.Round(EchoMs),
            EchoFeedback = Math.Round(EchoFeedback, 2),
            EchoMix = Math.Round(EchoMix, 2),
            ReverbMix = Math.Round(ReverbMix, 2),
            ReverbSize = Math.Round(ReverbSize, 2),
            OutputGainDb = Math.Round(OutputGainDb * 2) / 2,
        };
        if (preset == _current) return;
        _current = preset;
        _changed(preset);
    }
}
