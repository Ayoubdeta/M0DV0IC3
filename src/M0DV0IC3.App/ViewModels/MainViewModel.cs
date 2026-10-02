using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private static readonly DeviceItem NoMonitor = new(null, "(ninguno)", false);

    private readonly SettingsService _settings;
    private readonly AudioService _audio;
    private readonly DispatcherTimer _meterTimer;
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
    [ObservableProperty] private string _statusText = "Arrancando…";
    [ObservableProperty] private StatusKind _statusKind = StatusKind.Neutral;

    /// <summary>Etiqueta corta de la tarjeta de estado de la barra lateral: EN VIVO, SILENCIADO, PARADO...</summary>
    [ObservableProperty] private string _liveLabel = "PARADO";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _inputLevel;
    [ObservableProperty] private double _outputLevel;
    [ObservableProperty] private string _latencyText = "Latencia: —";
    [ObservableProperty] private string _latencyToolTip = "El audio está parado.";
    [ObservableProperty] private string? _underrunsText;
    [ObservableProperty] private string? _gateWarning;
    [ObservableProperty] private string _trayToolTip = "M0DV0IC3";

    /// <summary>No se guarda: al abrir la app nunca debe empezar silenciada sin que lo sepas.</summary>
    [ObservableProperty] private bool _muted;

    public MainViewModel(SettingsService settings, AudioService audio, HotkeyService hotkeys, Action restartAsAdmin)
    {
        _settings = settings;
        _audio = audio;
        var s = settings.Current;

        Hotkeys = new HotkeysViewModel(hotkeys, settings, restartAsAdmin);
        Voices = new VoicesViewModel(settings, Hotkeys);
        Soundboard = new SoundboardViewModel(settings, audio.Pipeline.Soundboard, Hotkeys);
        Music = new MusicViewModel(settings, audio);

        var pipeline = audio.Pipeline;
        pipeline.Voice.Range = s.VoiceRange;
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

        hotkeys.Pressed += (_, action) => OnHotkey(action);
        audio.StateChanged += (_, _) => UpdateStatus();
        audio.Devices.DevicesChanged += (_, _) => RefreshDevices();

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += OnMeterTick;
    }

    public HotkeysViewModel Hotkeys { get; }

    public VoicesViewModel Voices { get; }

    public SoundboardViewModel Soundboard { get; }

    public MusicViewModel Music { get; }

    public ObservableCollection<DeviceItem> InputDevices { get; } = [];

    public ObservableCollection<DeviceItem> OutputDevices { get; } = [];

    public ObservableCollection<DeviceItem> MonitorDevices { get; } = [];

    public bool HasMonitor => SelectedMonitor?.Id is not null;

    public IReadOnlyList<SafetyMarginOption> SafetyMarginOptions { get; } =
    [
        new(1, "1 ms (mínimo)"),
        new(2, "2 ms (recomendado)"),
        new(3, "3 ms"),
        new(5, "5 ms"),
        new(10, "10 ms (máxima estabilidad)"),
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

    /// <summary>Manda al pipeline la voz elegida, o la neutra si la voz está desactivada.</summary>
    public void ApplyVoice(bool liveEdit = false, bool force = false)
    {
        var preset = VoiceEnabled && Voices.Selected is { } card ? card.Preset : VoicePreset.Neutral;
        var voice = _audio.Pipeline.Voice;
        if (force || voice.CurrentPreset != preset) voice.SetPreset(preset, liveEdit);
        UpdateTrayToolTip();
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
        ? "Quita el ruido de fondo (teclado, ventilador, calle) con RNNoise. Añade +10 ms de latencia."
        : $"No disponible: {_audio.Pipeline.NoiseSuppressionError}";

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

    public string GateText => GateThresholdDb <= GateOffThreshold ? "apagada" : $"{GateThresholdDb:0} dB";

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
        UpdateLatency();
    }

    /// <summary>Al salir: para timers, sonidos y atajos (el motor lo para AudioService).</summary>
    public void Shutdown()
    {
        _meterTimer.Stop();
        Soundboard.StopAll();
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
            : "Todavía no se detecta VB-Cable. Si acabas de instalarlo y no aparece, reinicia el PC.";
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
            Replace(InputDevices, inputs.Select(d => new DeviceItem(d.Id, d.IsDefault ? $"{d.Name} (predeterminado)" : d.Name, d.IsVirtualCable)));
            Replace(OutputDevices, outputs.Select(d => new DeviceItem(d.Id, d.Id == cable?.Id ? $"★ {d.Name} — micrófono virtual" : d.Name, d.IsVirtualCable)));
            Replace(MonitorDevices, outputs.Where(d => !d.IsVirtualCable)
                .Select(d => new DeviceItem(d.Id, d.IsDefault ? $"{d.Name} (predeterminado)" : d.Name, false))
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
            ? "⚠ Esta salida no es VB-Cable: Discord y los juegos no recibirán tu voz por aquí, y si son altavoces puede haber acople (pitido)."
            : null;
    }

    private void UpdateCableSetup() => ShowCableSetup = !CableInstalled && SelectedOutput is null && !_cableSetupDismissed;

    private void UpdateStatus()
    {
        var state = _audio.State;
        IsRunning = state == EngineState.Running && _audio.Engine.IsRunning;

        if (SelectedInput is null)
        {
            SetStatus("No hay ningún micrófono conectado.", StatusKind.Warning);
        }
        else if (SelectedOutput is null && S.OutputDeviceId is not null)
        {
            // La salida que eligió el usuario existe en los ajustes pero no en Windows: se ha desconectado.
            SetStatus("La salida elegida no está conectada. Vuelve a conectarla (el audio seguirá solo) o elige otra.", StatusKind.Warning);
        }
        else if (SelectedOutput is null)
        {
            SetStatus(CableInstalled
                ? "Elige la salida (micrófono virtual) para empezar."
                : "Falta VB-Cable: instálalo para usar tu voz en Discord y juegos.", StatusKind.Warning);
        }
        else
        {
            switch (state)
            {
                case EngineState.Running when Muted:
                    SetStatus("● Funcionando, pero SILENCIADO: nadie te oye", StatusKind.Warning);
                    break;
                case EngineState.Running:
                    SetStatus(Music.StreamingName is { } music ? $"● Funcionando · 🎵 {music} por el micro" : "● Funcionando", StatusKind.Ok);
                    break;
                case EngineState.Error:
                    SetStatus($"⚠ {_audio.Error}", StatusKind.Error);
                    break;
                case EngineState.Starting:
                    SetStatus("Arrancando el audio…", StatusKind.Neutral);
                    break;
                default:
                    SetStatus(_audio.RequestedOptions is null ? "Parado" : "Arrancando el audio…", StatusKind.Neutral);
                    break;
            }
        }
        LiveLabel = state switch
        {
            EngineState.Running when Muted => "SILENCIADO",
            EngineState.Running => "EN VIVO",
            EngineState.Error => "ERROR",
            EngineState.Starting => "ARRANCANDO",
            _ => "PARADO",
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
        string voice = VoiceEnabled && Voices.Selected is { } card ? $"Voz: {card.Name}{(Voices.RandomEnabled ? " (aleatoria)" : "")}" : "Voz desactivada";
        string music = Music.StreamingName is { } name ? $" · {name} por el micro" : "";
        string audio = !IsRunning ? " (audio parado)" : Muted ? " (silenciado)" : "";
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
        }
    }

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
            ? $"⚠ Corta tu voz (llega a {_recentInputPeakDb:0} dB): bájala"
            : null;
    }

    private void UpdateLatency()
    {
        var engine = _audio.Engine;
        if (!engine.IsRunning)
        {
            LatencyText = "Latencia: —";
            LatencyToolTip = "El audio está parado.";
            UnderrunsText = null;
            return;
        }

        var report = engine.GetLatency();
        LatencyText = $"Latencia ≈ {report.TotalMs:0} ms";

        var text = new StringBuilder();
        text.AppendLine("Latencia que añade M0DV0IC3 (sin contar Discord ni la red):");
        text.AppendLine($"• Captura: {report.CaptureMs:0.0} ms{Describe(engine.CaptureInfo)}");
        text.AppendLine($"• Procesado: {report.ProcessingMs:0.0} ms{(NoiseSuppression ? " (incluye 10 ms de la supresión de ruido)" : "")}");
        text.AppendLine($"• Buffer: {report.BufferMs:0.0} ms");
        text.Append($"• Salida: {report.OutputMs:0.0} ms{Describe(engine.OutputInfo)}");
        if (engine.MonitorInfo is { } monitor)
            text.Append($"\nAuriculares: {monitor.DeviceName} ({monitor.ModeDescription}, periodo {monitor.PeriodMs:0.0} ms)");
        LatencyToolTip = text.ToString();

        int underruns = engine.Underruns;
        UnderrunsText = underruns > 0 ? $"cortes: {underruns}" : null;
    }

    private static string Describe(Audio.Wasapi.StreamInfo? info) =>
        info is null ? "" : $" — {info.ModeDescription}, periodo {info.PeriodMs:0.0} ms";

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
            case HotkeyActions.ToggleAppAudio:
                if (Music.IsSupported) Music.IsStreaming = !Music.IsStreaming;
                break;
            case HotkeyActions.StopSounds:
                Soundboard.StopAll();
                break;
            default:
                if (HotkeyActions.TryGetVoiceIndex(action, out int index))
                    Voices.ActivateIndex(index);
                else if (action.StartsWith(HotkeyActions.SoundPrefix, StringComparison.Ordinal))
                    Soundboard.PlayById(action[HotkeyActions.SoundPrefix.Length..]);
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
            Dialogs.Error($"No se pudo abrir {target}:\n{ex.Message}");
        }
    }
}
