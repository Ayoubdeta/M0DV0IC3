using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;
using M0DV0IC3.Dsp.Presets;

namespace M0DV0IC3.App.ViewModels;

public sealed record RandomIntervalOption(double Seconds, string Label);

/// <summary>Un grupo de voces incluidas (Autotune, Personajes...) con su título.</summary>
public sealed class VoiceGroupViewModel(VoiceCategory category, IReadOnlyList<VoiceCardViewModel> cards)
{
    public VoiceCategory Category { get; } = category;

    public string Title => VoiceCategories.Title(Category);

    public string Subtitle => VoiceCategories.Subtitle(Category);

    public IReadOnlyList<VoiceCardViewModel> Cards { get; } = cards;
}

/// <summary>Pestaña Voces: tarjetas de las voces incluidas y de las personalizadas, el editor y la voz aleatoria.</summary>
public sealed partial class VoicesViewModel : ObservableObject
{
    private const string CustomIcon = "🎭";

    private readonly SettingsService _settings;
    private readonly HotkeysViewModel _hotkeys;
    private readonly DispatcherTimer _randomTimer = new();
    private readonly Random _random = new();

    /// <summary>No se guarda: al abrir la app la voz nunca cambia sola sin que lo pidas.</summary>
    [ObservableProperty]
    private bool _randomEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DuplicateCommand), nameof(DeleteCommand))]
    private VoiceCardViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private VoiceEditorViewModel? _editor;

    public VoicesViewModel(SettingsService settings, HotkeysViewModel hotkeys)
    {
        _settings = settings;
        _hotkeys = hotkeys;

        // Las incluidas van agrupadas, y en ese mismo orden cuentan los atajos Voz 1..9.
        var cards = BuiltInVoices.All.Select(p => new VoiceCardViewModel(p)).ToList();
        foreach (var category in VoiceCategories.BuiltInOrder)
        {
            var group = cards.Where(c => c.Category == category).ToList();
            if (group.Count == 0) continue;
            foreach (var card in group) BuiltIn.Add(card);
            Groups.Add(new VoiceGroupViewModel(category, group));
        }

        // Las voces guardadas se corrigen por si el JSON se editó a mano (ids repetidos, IsBuiltIn...).
        var ids = new HashSet<string>(BuiltIn.Select(c => c.Id)) { VoicePreset.Neutral.Id };
        foreach (var saved in settings.Current.CustomVoices)
        {
            var preset = saved with { IsBuiltIn = false };
            if (string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id))
            {
                preset = preset with { Id = NewId() };
                ids.Add(preset.Id);
            }
            Custom.Add(new VoiceCardViewModel(preset));
        }
        settings.Current.CustomVoices = Custom.Select(c => c.Preset).ToList();

        var initial = AllCards.FirstOrDefault(c => c.Id == settings.Current.SelectedVoiceId) ?? BuiltIn[0];
        initial.IsSelected = true;
        _selected = initial;

        foreach (var binding in hotkeys.Actions)
            if (HotkeyActions.TryGetVoiceIndex(binding.ActionId, out _)) binding.GestureChanged += (_, _) => UpdateHotkeyHints();
        Custom.CollectionChanged += (_, _) =>
        {
            UpdateHotkeyHints();
            OnPropertyChanged(nameof(HasCustom));
        };
        UpdateHotkeyHints();

        _randomTimer.Tick += (_, _) => OnRandomTick();
    }

    /// <summary>El usuario ha elegido una voz (clic o atajo): la app activa la voz si estaba apagada.</summary>
    public event EventHandler? VoiceActivated;

    /// <summary>Se ha cambiado un parámetro en el editor (para oírlo en directo).</summary>
    public event EventHandler<VoiceCardViewModel>? PresetEdited;

    public ObservableCollection<VoiceCardViewModel> BuiltIn { get; } = [];

    /// <summary>Las voces incluidas por grupos, para la vista.</summary>
    public ObservableCollection<VoiceGroupViewModel> Groups { get; } = [];

    public ObservableCollection<VoiceCardViewModel> Custom { get; } = [];

    public bool HasCustom => Custom.Count > 0;

    public bool IsEditing => Editor is not null;

    public IReadOnlyList<RandomIntervalOption> RandomIntervals { get; } =
    [
        new(2, "cada 2 s"),
        new(5, "cada 5 s"),
        new(10, "cada 10 s"),
        new(30, "cada 30 s"),
        new(0, "al azar (2-10 s)"),
    ];

    /// <summary>Segundos entre cambios de la voz aleatoria (0 = al azar entre 2 y 10 s).</summary>
    public double RandomSeconds
    {
        get => _settings.Current.RandomVoiceSeconds;
        set
        {
            if (_settings.Current.RandomVoiceSeconds == value) return;
            _settings.Current.RandomVoiceSeconds = value;
            _settings.ScheduleSave();
            OnPropertyChanged();
            if (RandomEnabled) ScheduleRandom();
        }
    }

    /// <summary>Orden de la lista para los atajos Voz 1..9 y anterior/siguiente: incluidas y luego personalizadas.</summary>
    public IEnumerable<VoiceCardViewModel> AllCards => BuiltIn.Concat(Custom);

    public void ActivateIndex(int index)
    {
        var card = AllCards.ElementAtOrDefault(index);
        if (card is not null) Activate(card);
    }

    public void ActivateRelative(int delta)
    {
        var cards = AllCards.ToList();
        if (cards.Count == 0) return;
        int current = Selected is null ? -1 : cards.IndexOf(Selected);
        int next = current < 0 ? 0 : ((current + delta) % cards.Count + cards.Count) % cards.Count;
        Activate(cards[next]);
    }

    [RelayCommand]
    private void Activate(VoiceCardViewModel? card)
    {
        if (card is null) return;
        Select(card);
        VoiceActivated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleRandom() => RandomEnabled = !RandomEnabled;

    partial void OnRandomEnabledChanged(bool value)
    {
        if (!value)
        {
            _randomTimer.Stop();
            return;
        }
        PickRandom(activate: true);
        ScheduleRandom();
    }

    private void ScheduleRandom()
    {
        _randomTimer.Stop();
        double seconds = RandomSeconds > 0 ? RandomSeconds : 2 + _random.NextDouble() * 8;
        _randomTimer.Interval = TimeSpan.FromSeconds(seconds);
        _randomTimer.Start();
    }

    private void OnRandomTick()
    {
        // Con el editor abierto no se cambia: cerraría la voz que estás editando.
        if (!IsEditing) PickRandom(activate: false);
        ScheduleRandom();
    }

    /// <summary>
    /// Elige otra voz al azar. Al activar el modo se enciende también la voz; en los cambios siguientes no, para no
    /// volver a encenderla si la apagas. La voz invertida se salta: sus 200 ms de retardo dejarían un hueco en cada cambio.
    /// </summary>
    private void PickRandom(bool activate)
    {
        var pool = AllCards.Where(c => c != Selected && !c.Preset.UsesReverse).ToList();
        if (pool.Count == 0) return;
        var card = pool[_random.Next(pool.Count)];
        if (activate) Activate(card);
        else Select(card);
    }

    [RelayCommand]
    private void NewVoice()
    {
        var preset = VoicePreset.Neutral with { Id = NewId(), Name = UniqueName("Mi voz"), Icon = CustomIcon, IsBuiltIn = false };
        AddCustomAndEdit(preset);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (Selected is not { IsBuiltIn: false } card) return;
        Activate(card);
        OpenEditor(card);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (Selected is not { } card) return;
        var preset = card.Preset with { Id = NewId(), Name = UniqueName($"{card.Name} (copia)"), IsBuiltIn = false };
        AddCustomAndEdit(preset);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Delete()
    {
        if (Selected is not { IsBuiltIn: false } card) return;
        if (!Dialogs.Confirm($"¿Borrar la voz «{card.Name}»? No se puede deshacer.")) return;

        if (Editor?.Card == card) CloseEditor();
        int index = Custom.IndexOf(card);
        Custom.Remove(card);
        SaveCustomVoices();

        var next = Custom.Count > 0 ? Custom[Math.Clamp(index, 0, Custom.Count - 1)] : BuiltIn[0];
        Select(next);
    }

    private bool CanEdit() => Selected is { IsBuiltIn: false };

    private bool HasSelection() => Selected is not null;

    private void AddCustomAndEdit(VoicePreset preset)
    {
        var card = new VoiceCardViewModel(preset);
        Custom.Add(card);
        SaveCustomVoices();
        Activate(card);
        OpenEditor(card);
    }

    private void Select(VoiceCardViewModel card)
    {
        if (Editor is not null && Editor.Card != card) CloseEditor();
        if (Selected == card) return;
        if (Selected is not null) Selected.IsSelected = false;
        card.IsSelected = true;
        Selected = card;
        _settings.Current.SelectedVoiceId = card.Id;
        _settings.ScheduleSave();
    }

    private void OpenEditor(VoiceCardViewModel card)
    {
        Editor = new VoiceEditorViewModel(card, preset =>
        {
            card.Preset = preset;
            SaveCustomVoices();
            PresetEdited?.Invoke(this, card);
        }, CloseEditor);
    }

    private void CloseEditor() => Editor = null;

    private void SaveCustomVoices()
    {
        _settings.Current.CustomVoices = Custom.Select(c => c.Preset).ToList();
        _settings.ScheduleSave();
    }

    private void UpdateHotkeyHints()
    {
        int index = 0;
        foreach (var card in AllCards)
        {
            card.HotkeyHint = index < 9 ? _hotkeys.Find(HotkeyActions.VoiceAction(index + 1))?.Gesture?.DisplayText : null;
            index++;
        }
    }

    private string UniqueName(string baseName)
    {
        var names = new HashSet<string>(AllCards.Select(c => c.Name), StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (int n = 2; ; n++)
        {
            string candidate = $"{baseName} {n}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private static string NewId() => "custom-" + Guid.NewGuid().ToString("N")[..12];
}
