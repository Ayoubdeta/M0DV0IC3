using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Localization;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio;
using M0DV0IC3.Dsp.Dynamics;
using M0DV0IC3.Dsp.Pitch;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.App.ViewModels;

public enum StatusKind
{
    Neutral,
    Ok,
    Warning,
    Error,
}

public sealed record SafetyMarginOption(double Ms, string Label);

/// <summary>Un idioma de la interfaz, con su nombre en ese idioma ("Español", "English").</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// Ventana principal: dispositivos, estado del motor, medidores, opciones de la barra inferior y de Ajustes.
/// Los ajustes guardados son la fuente de verdad de las opciones; cada cambio se aplica al pipeline,
/// se guarda y, si afecta a los dispositivos, reinicia el motor.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const double MeterFloorDb = -60;
    private const double MeterFallDbPerTick = 0.8;
    private const double GateOffThreshold = NoiseGate.DisabledThresholdDb + 0.5;

    // Aviso de puerta: el pico más alto del micro baja ~0,6 dB/s, así que recuerda lo que dijiste en el último medio minuto.
    // Por encima de -45 dBFS es voz (el ruido de fondo casi nunca llega); si aun así no alcanza el umbral, la puerta no se abre.
    private const double SpeechPeakDb = -45;
    private const double RecentPeakFallDbPerTick = 0.02;

    private static readonly DeviceItem NoMonitor = new(null, Loc.T("(ninguno)"), false);

    private readonly SettingsService _settings;
    private readonly AudioService _audio;
    private readonly Action _restart;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _holdTimer;
    private readonly AppDucking _ducking;
    private VoiceCardViewModel? _holding;
    private uint _holdKey;
    private bool _refreshingDevices;
    private bool _cableSetupDismissed;
    private int _meterTicks;
    private double _inputDb = MeterFloorDb;
    private double _outputDb = MeterFloorDb;
    private double _recentInputPeakDb = MeterFloorDb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMonitor))]
    private DeviceItem? _selectedMonitor;

    [ObservableProperty] private DeviceItem? _selectedInput;
    [ObservableProperty] private DeviceItem? _selectedOutput;
    [ObservableProperty] private bool _cableInstalled = true;
    [ObservableProperty] private bool _showCableSetup;
    [ObservableProperty] private string? _cableCheckMessage;
    [ObservableProperty] private string? _outputWarning;
    [ObservableProperty] private string _statusText = Loc.T("Arrancando…");
    [ObservableProperty] private string _learnedPitchText = "";
    [ObservableProperty] private StatusKind _statusKind = StatusKind.Neutral;

    /// <summary>Etiqueta corta de la tarjeta de estado de la barra lateral: EN VIVO, SILENCIADO, PARADO...</summary>
    [ObservableProperty] private string _liveLabel = Loc.T("PARADO");
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _inputLevel;
    [ObservableProperty] private double _outputLevel;
    [ObservableProperty] private string _latencyText = Loc.T("Latencia: —");
    [ObservableProperty] private string _latencyToolTip = Loc.T("El audio está parado.");
    [ObservableProperty] private string? _underrunsText;
    [ObservableProperty] private string? _gateWarning;
    [ObservableProperty] private string _trayToolTip = "M0DV0IC3";

    /// <summary>No se guarda: al abrir la app nunca debe empezar silenciada sin que lo sepas.</summary>
    [ObservableProperty] private bool _muted;

    public MainViewModel(SettingsService settings, AudioService audio, HotkeyService hotkeys, Action restartAsAdmin, Action restart)
    {
        _settings = settings;
        _audio = audio;
        _restart = restart;
        var s = settings.Current;

        Hotkeys = new HotkeysViewModel(hotkeys, settings, restartAsAdmin);
        Voices = new VoicesViewModel(settings, Hotkeys);
        Soundboard = new SoundboardViewModel(settings, audio.Pipeline.Soundboard, Hotkeys);
        Speech = new SpeechViewModel(settings, audio.Pipeline.Speech, Hotkeys);
        Recorder = new RecorderViewModel(audio.Pipeline.Recorder, Soundboard);
        _ducking = new AppDucking(settings, audio.AppAudio);
        Music = new MusicViewModel(settings, audio, Voices, _ducking, () => HasMonitor);
        Karaoke = new KaraokeViewModel(settings, audio, Music, Voices, _ducking, () => HasMonitor);

        var pipeline = audio.Pipeline;
        pipeline.Voice.Range = s.VoiceRange;
        if (s.LearnedPitchHz > 0) pipeline.Voice.Profile.Seed(s.LearnedPitchHz);
        UpdateLearnedPitch();
        pipeline.MonitorVoice = s.MonitorVoice;
        pipeline.MonitorSounds = s.MonitorSounds;
        pipeline.NoiseSuppression = s.NoiseSuppression && pipeline.NoiseSuppressionAvailable;
        pipeline.Gate.ThresholdDb = s.GateThresholdDb;

        Voices.PropertyChanged += OnVoicesPropertyChanged;
        Music.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MusicViewModel.StreamingName)) UpdateStatus();
        };
        Voices.VoiceActivated += (_, _) =>
        {
            if (!VoiceEnabled) VoiceEnabled = true;
            else ApplyVoice();
        };
        Voices.PresetEdited += (_, card) =>
        {
            if (VoiceEnabled && card == Voices.Selected) ApplyVoice(liveEdit: true);
        };
        Speech.WithVoiceChanged += (_, _) => ApplyVoice();

        hotkeys.Pressed += (_, action) => OnHotkey(action);
        audio.StateChanged += (_, _) => UpdateStatus();
        audio.Devices.DevicesChanged += (_, _) => RefreshDevices();

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += OnMeterTick;

        // «Mantener pulsado»: Windows avisa al pulsar el atajo, pero no al soltarlo; se mira la tecla cada 15 ms.
        _holdTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _holdTimer.Tick += OnHoldTick;
    }

    public HotkeysViewModel Hotkeys { get; }

    public VoicesViewModel Voices { get; }

    public SoundboardViewModel Soundboard { get; }

    public SpeechViewModel Speech { get; }

    public RecorderViewModel Recorder { get; }

    public MusicViewModel Music { get; }

    public KaraokeViewModel Karaoke { get; }

    /// <summary>Pestaña de la barra lateral que se ve (la del karaoke es la 4).</summary>
    public int SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (_selectedPage == value) return;
            _selectedPage = value;
            OnPropertyChanged();
            Karaoke.SetPageVisible(value == KaraokePage);
        }
    }

    private const int KaraokePage = 4;
    private int _selectedPage;

    public ObservableCollection<DeviceItem> InputDevices { get; } = [];

    public ObservableCollection<DeviceItem> OutputDevices { get; } = [];

    public ObservableCollection<DeviceItem> MonitorDevices { get; } = [];

    public bool HasMonitor => SelectedMonitor?.Id is not null;

    public IReadOnlyList<SafetyMarginOption> SafetyMarginOptions { get; } =
    [
        new(1, Loc.T("1 ms (mínimo)")),
        new(2, Loc.T("2 ms (recomendado)")),
        new(3, "3 ms"),
        new(5, "5 ms"),
        new(10, Loc.T("10 ms (máxima estabilidad)")),
    ];

    public string DataFolder => AppPaths.DataFolder;

    private AppSettings S => _settings.Current;

    // ---- Voz ----

    public bool VoiceEnabled
    {
        get => S.VoiceEnabled;
        set
        {
            if (Set(S.VoiceEnabled, value, v => S.VoiceEnabled = v)) ApplyVoice();
        }
    }

    [RelayCommand]
    private void ToggleVoice() => VoiceEnabled = !VoiceEnabled;

    /// <summary>
    /// Manda al pipeline la voz elegida, o la neutra si la voz está desactivada. Mientras se mantiene pulsado el atajo
    /// «Mantener pulsado», manda la de ese atajo. Las frases de texto a voz llevan la misma (si así se pide).
    /// </summary>
    public void ApplyVoice(bool liveEdit = false, bool force = false)
    {
        var preset = _holding is { } held ? held.Preset
            : VoiceEnabled && Voices.Selected is { } card ? card.Preset
            : VoicePreset.Neutral;
        var voice = _audio.Pipeline.Voice;
        if (force || voice.CurrentPreset != preset) voice.SetPreset(preset, liveEdit);

        var speechPreset = Speech.WithVoice ? preset : VoicePreset.Neutral;
        var speech = _audio.Pipeline.Speech.Voice;
        if (force || speech.CurrentPreset != speechPreset) speech.SetPreset(speechPreset, liveEdit);
        UpdateTrayToolTip();
    }

    private void BeginHold()
    {
        if (_holding is not null || Voices.HoldVoice is not { } card) return;
        if (Hotkeys.Find(HotkeyActions.HoldVoice)?.Gesture is not { } gesture) return;
        _holdKey = gesture.VirtualKey;
        _holding = card;
        ApplyVoice();
        _holdTimer.Start();
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        if ((NativeMethods.GetAsyncKeyState((int)_holdKey) & 0x8000) != 0) return;
        _holdTimer.Stop();
        _holding = null;
        ApplyVoice();
    }

    // ---- Barra inferior ----

    partial void OnMutedChanged(bool value)
    {
        _audio.Pipeline.Muted = value;
        UpdateStatus();
    }

    public bool MonitorVoice
    {
        get => S.MonitorVoice;
        set
        {
            if (Set(S.MonitorVoice, value, v => S.MonitorVoice = v)) _audio.Pipeline.MonitorVoice = value;
        }
    }

    public bool MonitorSounds
    {
        get => S.MonitorSounds;
        set
        {
            if (Set(S.MonitorSounds, value, v => S.MonitorSounds = v)) _audio.Pipeline.MonitorSounds = value;
        }
    }

    public bool NoiseSuppressionAvailable => _audio.Pipeline.NoiseSuppressionAvailable;

    public bool NoiseSuppression
    {
        get => S.NoiseSuppression && NoiseSuppressionAvailable;
        set
        {
            if (!NoiseSuppressionAvailable) return;
            if (Set(S.NoiseSuppression, value, v => S.NoiseSuppression = v)) _audio.Pipeline.NoiseSuppression = value;
        }
    }

    public string NoiseSuppressionToolTip => NoiseSuppressionAvailable
        ? Loc.T("Quita el ruido de fondo (teclado, ventilador, calle) con RNNoise. Añade +10 ms de latencia.")
        : Loc.F("No disponible: {0}", _audio.Pipeline.NoiseSuppressionError);

    public double GateThresholdDb
    {
        get => S.GateThresholdDb;
        set
        {
            value = Math.Clamp(Math.Round(value), NoiseGate.DisabledThresholdDb, -20);
            if (!Set(S.GateThresholdDb, value, v => S.GateThresholdDb = v)) return;
            _audio.Pipeline.Gate.ThresholdDb = value;
            OnPropertyChanged(nameof(GateText));
            UpdateGateWarning();
        }
    }

    public string GateText => GateThresholdDb <= GateOffThreshold ? Loc.T("apagada") : $"{GateThresholdDb:0} dB";

    // ---- Ajustes ----

    public bool IsRangeLow
    {
        get => S.VoiceRange == VoiceRange.Low;
        set
        {
            if (value) SetVoiceRange(VoiceRange.Low);
        }
    }

    public bool IsRangeMedium
    {
        get => S.VoiceRange == VoiceRange.Medium;
        set
        {
            if (value) SetVoiceRange(VoiceRange.Medium);
        }
    }

    public bool IsRangeHigh
    {
        get => S.VoiceRange == VoiceRange.High;
        set
        {
            if (value) SetVoiceRange(VoiceRange.High);
        }
    }

    public bool Exclusive
    {
        get => S.Exclusive;
        set
        {
            if (Set(S.Exclusive, value, v => S.Exclusive = v)) RestartEngine();
        }
    }

    public bool PreferLowLatency
    {
        get => S.PreferLowLatency;
        set
        {
            if (Set(S.PreferLowLatency, value, v => S.PreferLowLatency = v)) RestartEngine();
        }
    }

    public double SafetyMarginMs
    {
        get => S.SafetyMarginMs;
        set
        {
            if (Set(S.SafetyMarginMs, value, v => S.SafetyMarginMs = v)) RestartEngine();
        }
    }

    public IReadOnlyList<LanguageOption> Languages { get; } = [new("es", "Español"), new("en", "English")];

    /// <summary>
    /// Idioma de la interfaz. Se aplica al reiniciar: la app pregunta si reiniciar ya (en los dos idiomas, porque el que
    /// acabas de elegir no es el que ves).
    /// </summary>
    public string LanguageCode
    {
        get => S.Language ?? Loc.Code(Loc.Language);
        set
        {
            if (!Set(S.Language, value, v => S.Language = v)) return;
            _settings.SaveNow();
            OnPropertyChanged(nameof(LanguageRestartPending));
            if (!LanguageRestartPending) return;
            if (Dialogs.Confirm("Hay que reiniciar M0DV0IC3 para cambiar el idioma. ¿Reiniciar ahora?\n\n"
                + "M0DV0IC3 needs to restart to change the language. Restart now?")) _restart();
        }
    }

    /// <summary>Se ha elegido otro idioma pero aún no se ha reiniciado.</summary>
    public bool LanguageRestartPending => Loc.Parse(S.Language) != Loc.Language;

    [RelayCommand]
    private void RestartNow() => _restart();

    public bool MinimizeToTray
    {
        get => S.MinimizeToTray;
        set => Set(S.MinimizeToTray, value, v => S.MinimizeToTray = v);
    }

    public bool StartMinimized
    {
        get => S.StartMinimized;
        set => Set(S.StartMinimized, value, v => S.StartMinimized = v);
    }

    // ---- Ciclo de vida ----

    /// <summary>Primera carga: dispositivos, atajos, voz, motor y sonidos.</summary>
    public void Initialize()
    {
        RefreshDevices();
        Hotkeys.RegisterAll();
        ApplyVoice(force: true);
        RestartEngine();
        _ = Soundboard.LoadSavedSoundsAsync();
        _ = Speech.PrepareSavedPhrasesAsync();
        UpdateLatency();
    }

    /// <summary>Al salir: para timers, sonidos y atajos (el motor lo para AudioService).</summary>
    public void Shutdown()
    {
        Karaoke.Shutdown();
        Music.Shutdown();
        _ducking.Shutdown();
        Recorder.Shutdown();
        _holdTimer.Stop();
        var profile = _audio.Pipeline.Voice.Profile;
        if (profile.IsLearned) _settings.Current.LearnedPitchHz = Math.Round(profile.CenterHz, 1);
        _meterTimer.Stop();
        Soundboard.StopAll();
        Speech.Stop();
        Hotkeys.UnregisterAll();
    }

    /// <summary>Los medidores solo se refrescan con la ventana visible.</summary>
    public void SetWindowVisible(bool visible)
    {
        if (visible)
        {
            _meterTimer.Start();
        }
        else
        {
            _meterTimer.Stop();
            InputLevel = OutputLevel = 0;
            _inputDb = _outputDb = MeterFloorDb;
        }
    }

    // ---- VB-Cable ----

    [RelayCommand]
    private void OpenCableDownload() => OpenUrl(DeviceService.CableDownloadUrl);

    [RelayCommand]
    private void RecheckCable()
    {
        _cableSetupDismissed = false;
        RefreshDevices();
        CableCheckMessage = CableInstalled
            ? null
            : Loc.T("Todavía no se detecta VB-Cable. Si acabas de instalarlo y no aparece, reinicia el PC.");
    }

    [RelayCommand]
    private void DismissCableSetup()
    {
        _cableSetupDismissed = true;
        UpdateCableSetup();
    }

    [RelayCommand]
    private void ShowCableHelp()
    {
        _cableSetupDismissed = false;
        CableCheckMessage = null;
        ShowCableSetup = true;
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenUrl(AppPaths.DataFolder);

    // ---- Dispositivos ----

    partial void OnSelectedInputChanged(DeviceItem? value)
    {
        if (_refreshingDevices) return;
        S.InputDeviceId = value?.Id;
        _settings.ScheduleSave();
        RestartEngine();
    }

    partial void OnSelectedOutputChanged(DeviceItem? value)
    {
        if (_refreshingDevices) return;
        S.OutputDeviceId = value?.Id;
        _settings.ScheduleSave();
        UpdateOutputWarning();
        UpdateCableSetup();
        RestartEngine();
    }

    partial void OnSelectedMonitorChanged(DeviceItem? value)
    {
        Music?.RefreshMonitorWarning();
        if (_refreshingDevices) return;
        S.MonitorDeviceId = value?.Id;
        _settings.ScheduleSave();
        RestartEngine();
    }

    /// <summary>Rellena los desplegables manteniendo lo elegido por Id; reinicia el motor si cambia algo.</summary>
    private void RefreshDevices()
    {
        IReadOnlyList<AudioDeviceInfo> inputs, outputs;
        AudioDeviceInfo? cable;
        try
        {
            inputs = _audio.Devices.GetInputDevices();
            outputs = _audio.Devices.GetOutputDevices();
            cable = _audio.Devices.FindCableInput();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron enumerar los dispositivos de audio", ex);
            inputs = outputs = [];
            cable = null;
        }

        _refreshingDevices = true;
        try
        {
            Replace(InputDevices, inputs.Select(d => new DeviceItem(d.Id, d.IsDefault ? Loc.F("{0} (predeterminado)", d.Name) : d.Name, d.IsVirtualCable)));
            Replace(OutputDevices, outputs.Select(d => new DeviceItem(d.Id, d.Id == cable?.Id ? Loc.F("★ {0} — micrófono virtual", d.Name) : d.Name, d.IsVirtualCable)));
            Replace(MonitorDevices, outputs.Where(d => !d.IsVirtualCable)
                .Select(d => new DeviceItem(d.Id, d.IsDefault ? Loc.F("{0} (predeterminado)", d.Name) : d.Name, false))
                .Prepend(NoMonitor));

            SelectedInput = InputDevices.FirstOrDefault(d => d.Id == S.InputDeviceId)
                ?? InputDevices.FirstOrDefault(d => inputs.Any(i => i.Id == d.Id && i.IsDefault))
                ?? InputDevices.FirstOrDefault();

            // SEGURIDAD: solo se elige sola la salida VB-Cable. Nunca unos altavoces: tu voz sonaría por ellos y se acoplaría.
            SelectedOutput = OutputDevices.FirstOrDefault(d => d.Id == S.OutputDeviceId)
                ?? (cable is null ? null : OutputDevices.FirstOrDefault(d => d.Id == cable.Id));

            SelectedMonitor = MonitorDevices.FirstOrDefault(d => d.Id is not null && d.Id == S.MonitorDeviceId) ?? NoMonitor;
            CableInstalled = cable is not null;
            if (CableInstalled) CableCheckMessage = null;
        }
        finally
        {
            _refreshingDevices = false;
        }

        UpdateOutputWarning();
        UpdateCableSetup();

        var options = BuildOptions();
        if (options != _audio.RequestedOptions || (options is not null && _audio.State is EngineState.Error or EngineState.Stopped))
            RestartEngine();
        else
            UpdateStatus();
    }

    private EngineOptions? BuildOptions()
    {
        if (SelectedInput?.Id is not { } input || SelectedOutput?.Id is not { } output) return null;
        return new EngineOptions
        {
            InputDeviceId = input,
            OutputDeviceId = output,
            MonitorDeviceId = SelectedMonitor?.Id,
            Exclusive = S.Exclusive,
            PreferLowLatency = S.PreferLowLatency,
            SafetyMarginMs = S.SafetyMarginMs,
        };
    }

    private void RestartEngine()
    {
        _audio.RequestRestart(BuildOptions());
        UpdateStatus();
    }

    private void UpdateOutputWarning()
    {
        OutputWarning = SelectedOutput is { IsVirtualCable: false }
            ? Loc.T("⚠ Esta salida no es VB-Cable: Discord y los juegos no recibirán tu voz por aquí, y si son altavoces puede haber acople (pitido).")
            : null;
    }

    private void UpdateCableSetup() => ShowCableSetup = !CableInstalled && SelectedOutput is null && !_cableSetupDismissed;

    private void UpdateStatus()
    {
        var state = _audio.State;
        IsRunning = state == EngineState.Running && _audio.Engine.IsRunning;
        Recorder.EngineRunning = IsRunning;

        if (SelectedInput is null)
        {
            SetStatus(Loc.T("No hay ningún micrófono conectado."), StatusKind.Warning);
        }
        else if (SelectedOutput is null && S.OutputDeviceId is not null)
        {
            // La salida que eligió el usuario existe en los ajustes pero no en Windows: se ha desconectado.
            SetStatus(Loc.T("La salida elegida no está conectada. Vuelve a conectarla (el audio seguirá solo) o elige otra."), StatusKind.Warning);
        }
        else if (SelectedOutput is null)
        {
            SetStatus(CableInstalled
                ? Loc.T("Elige la salida (micrófono virtual) para empezar.")
                : Loc.T("Falta VB-Cable: instálalo para usar tu voz en Discord y juegos."), StatusKind.Warning);
        }
        else
        {
            switch (state)
            {
                case EngineState.Running when Muted:
                    SetStatus(Loc.T("● Funcionando, pero SILENCIADO: nadie te oye"), StatusKind.Warning);
                    break;
                case EngineState.Running:
                    SetStatus(Music.StreamingName is { } music ? Loc.F("● Funcionando · 🎵 {0} por el micro", music) : Loc.T("● Funcionando"), StatusKind.Ok);
                    break;
                case EngineState.Error:
                    SetStatus($"⚠ {_audio.Error}", StatusKind.Error);
                    break;
                case EngineState.Starting:
                    SetStatus(Loc.T("Arrancando el audio…"), StatusKind.Neutral);
                    break;
                default:
                    SetStatus(_audio.RequestedOptions is null ? Loc.T("Parado") : Loc.T("Arrancando el audio…"), StatusKind.Neutral);
                    break;
            }
        }
        LiveLabel = state switch
        {
            EngineState.Running when Muted => Loc.T("SILENCIADO"),
            EngineState.Running => Loc.T("EN VIVO"),
            EngineState.Error => Loc.T("ERROR"),
            EngineState.Starting => Loc.T("ARRANCANDO"),
            _ => Loc.T("PARADO"),
        };
        UpdateLatency();
        UpdateTrayToolTip();
    }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText = text;
        StatusKind = kind;
    }

    private void UpdateTrayToolTip()
    {
        string voice = VoiceEnabled && Voices.Selected is { } card
            ? (Voices.RandomEnabled ? Loc.F("Voz: {0} (aleatoria)", card.Name) : Loc.F("Voz: {0}", card.Name))
            : Loc.T("Voz desactivada");
        string music = Music.StreamingName is { } name ? Loc.F(" · {0} por el micro", name) : "";
        string audio = !IsRunning ? Loc.T(" (audio parado)") : Muted ? Loc.T(" (silenciado)") : "";
        TrayToolTip = $"M0DV0IC3 — {voice}{music}{audio}";
    }

    // ---- Medidores y latencia ----

    private void OnMeterTick(object? sender, EventArgs e)
    {
        var pipeline = _audio.Pipeline;
        float inputPeak = pipeline.ReadInputPeak();
        _inputDb = Fall(_inputDb, inputPeak);
        double inputPeakDb = inputPeak > 0 ? 20 * Math.Log10(inputPeak) : MeterFloorDb;
        _recentInputPeakDb = Math.Max(inputPeakDb, _recentInputPeakDb - RecentPeakFallDbPerTick);
        _outputDb = Fall(_outputDb, pipeline.ReadOutputPeak());
        Music.UpdateLevel(pipeline.ReadAppAudioPeak());
        InputLevel = (_inputDb - MeterFloorDb) / -MeterFloorDb;
        OutputLevel = (_outputDb - MeterFloorDb) / -MeterFloorDb;

        // La latencia, los cortes y el aviso de la puerta, unas dos veces por segundo.
        if (++_meterTicks % 15 == 0)
        {
            UpdateLatency();
            UpdateGateWarning();
            UpdateLearnedPitch();
        }
    }

    /// <summary>El tono medio que usan las voces con tono objetivo (Mujer, Niño, Grave…), en Ajustes.</summary>
    private void UpdateLearnedPitch()
    {
        var profile = _audio.Pipeline.Voice.Profile;
        LearnedPitchText = !profile.IsLearned
            ? Loc.T("Tu tono medio: todavía no lo sé. Habla un poco con una voz como Mujer, Grave o Niño y lo aprenderé.")
            : Loc.F("Tu tono medio: {0:0} Hz ({1}). Voces como Mujer, Niño o Grave lo usan para saber cuánto cambiar tu tono.", profile.CenterHz, DescribePitch(profile.CenterHz));
    }

    private static string DescribePitch(double hz) => hz switch
    {
        < 105 => Loc.T("voz de hombre grave"),
        < 150 => Loc.T("voz de hombre"),
        < 190 => Loc.T("voz aguda de hombre o grave de mujer"),
        < 260 => Loc.T("voz de mujer"),
        _ => Loc.T("voz muy aguda"),
    };

    /// <summary>Sube al instante y cae unos 24 dB/s, como un vúmetro.</summary>
    private static double Fall(double previousDb, float peak)
    {
        double db = peak > 0 ? Math.Max(MeterFloorDb, 20 * Math.Log10(peak)) : MeterFloorDb;
        return db >= previousDb ? db : Math.Max(db, previousDb - MeterFallDbPerTick);
    }

    /// <summary>Avisa si la puerta de ruido está tan alta que tu voz no llega a abrirla (no te oiría nadie).</summary>
    private void UpdateGateWarning()
    {
        double threshold = GateThresholdDb;
        bool blocking = threshold > GateOffThreshold && IsRunning
            && _recentInputPeakDb > SpeechPeakDb && _recentInputPeakDb < threshold - 1;
        GateWarning = blocking
            ? Loc.F("⚠ Corta tu voz (llega a {0:0} dB): bájala", _recentInputPeakDb)
            : null;
    }

    private void UpdateLatency()
    {
        var engine = _audio.Engine;
        if (!engine.IsRunning)
        {
            LatencyText = Loc.T("Latencia: —");
            LatencyToolTip = Loc.T("El audio está parado.");
            UnderrunsText = null;
            return;
        }

        var report = engine.GetLatency();
        LatencyText = Loc.F("Latencia ≈ {0:0} ms", report.TotalMs);

        var text = new StringBuilder();
        text.AppendLine(Loc.T("Latencia que añade M0DV0IC3 (sin contar Discord ni la red):"));
        text.AppendLine(Loc.F("• Captura: {0:0.0} ms{1}", report.CaptureMs, Describe(engine.CaptureInfo)));
        text.AppendLine(Loc.F("• Procesado: {0:0.0} ms{1}", report.ProcessingMs, NoiseSuppression ? Loc.T(" (incluye 10 ms de la supresión de ruido)") : ""));
        text.AppendLine(Loc.F("• Buffer: {0:0.0} ms", report.BufferMs));
        text.Append(Loc.F("• Salida: {0:0.0} ms{1}", report.OutputMs, Describe(engine.OutputInfo)));
        if (engine.MonitorInfo is { } monitor)
            text.Append("\n" + Loc.F("Auriculares: {0} ({1}, periodo {2:0.0} ms)", monitor.DeviceName, monitor.ModeDescription, monitor.PeriodMs));
        LatencyToolTip = text.ToString();

        int underruns = engine.Underruns;
        UnderrunsText = underruns > 0 ? Loc.F("cortes: {0}", underruns) : null;
    }

    private static string Describe(Audio.Wasapi.StreamInfo? info) =>
        info is null ? "" : Loc.F(" — {0}, periodo {1:0.0} ms", info.ModeDescription, info.PeriodMs);

    // ---- Atajos ----

    private void OnHotkey(string action)
    {
        switch (action)
        {
            case HotkeyActions.ToggleVoice:
                VoiceEnabled = !VoiceEnabled;
                break;
            case HotkeyActions.ToggleMute:
                Muted = !Muted;
                break;
            case HotkeyActions.ToggleMonitor:
                MonitorVoice = !MonitorVoice;
                break;
            case HotkeyActions.ToggleNoise:
                NoiseSuppression = !NoiseSuppression;
                break;
            case HotkeyActions.PrevVoice:
                Voices.ActivateRelative(-1);
                break;
            case HotkeyActions.NextVoice:
                Voices.ActivateRelative(1);
                break;
            case HotkeyActions.ToggleRandomVoice:
                Voices.RandomEnabled = !Voices.RandomEnabled;
                break;
            case HotkeyActions.HoldVoice:
                BeginHold();
                break;
            case HotkeyActions.ToggleAppAudio:
                if (Music.IsSupported) Music.IsStreaming = !Music.IsStreaming;
                break;
            case HotkeyActions.ToggleKaraoke:
                if (Karaoke.IsSupported) Karaoke.IsOn = !Karaoke.IsOn;
                break;
            case HotkeyActions.ToggleRecording:
                Recorder.Toggle();
                break;
            case HotkeyActions.StopSounds:
                Soundboard.StopAll();
                Speech.Stop();
                break;
            default:
                if (HotkeyActions.TryGetVoiceIndex(action, out int index))
                    Voices.ActivateIndex(index);
                else if (action.StartsWith(HotkeyActions.SoundPrefix, StringComparison.Ordinal))
                    Soundboard.PlayById(action[HotkeyActions.SoundPrefix.Length..]);
                else if (action.StartsWith(HotkeyActions.PhrasePrefix, StringComparison.Ordinal))
                    Speech.PlayById(action[HotkeyActions.PhrasePrefix.Length..]);
                break;
        }
    }

    // ---- Utilidades ----

    private void OnVoicesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VoicesViewModel.Selected)) ApplyVoice();
        else if (e.PropertyName == nameof(VoicesViewModel.RandomEnabled)) UpdateTrayToolTip();
    }

    private void SetVoiceRange(VoiceRange range)
    {
        if (S.VoiceRange == range) return;
        S.VoiceRange = range;
        _settings.ScheduleSave();
        _audio.Pipeline.Voice.Range = range;
        // Las cadenas con PSOLA se construyen con el rango: hay que volver a crearla.
        ApplyVoice(force: true);
        OnPropertyChanged(nameof(IsRangeLow));
        OnPropertyChanged(nameof(IsRangeMedium));
        OnPropertyChanged(nameof(IsRangeHigh));
    }

    /// <summary>Cambia un ajuste guardado, avisa a la vista y programa el guardado. Devuelve false si no cambió.</summary>
    private bool Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        OnPropertyChanged(propertyName);
        _settings.ScheduleSave();
        return true;
    }

    private static void Replace(ObservableCollection<DeviceItem> target, IEnumerable<DeviceItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private static void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warn($"No se pudo abrir {target}", ex);
            Dialogs.Error(Loc.F("No se pudo abrir {0}:\n{1}", target, ex.Message));
        }
    }
}
