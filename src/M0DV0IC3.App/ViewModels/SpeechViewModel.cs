using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Localization;
using M0DV0IC3.App.Models;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.Speech;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Una frase guardada de texto a voz: se dice con su botón o con su atajo.</summary>
public sealed partial class PhraseItemViewModel(SpeechViewModel owner, PhraseEntry entry, HotkeyBindingViewModel hotkey) : ObservableObject
{
    public PhraseEntry Entry { get; } = entry;

    public string Id => Entry.Id;

    public string Text => Entry.Text;

    public HotkeyBindingViewModel Hotkey { get; } = hotkey;

    [RelayCommand]
    private Task Say() => owner.SayAsync(Text);

    [RelayCommand]
    private void Delete() => owner.Delete(this);
}

/// <summary>
/// Pestaña Texto a voz: lo que escribes lo dice una voz de Windows y suena por el micro, con la voz que tengas puesta.
/// Las frases guardadas se sintetizan de antemano para que sus atajos suenen al instante.
/// </summary>
public sealed partial class SpeechViewModel : ObservableObject
{
    private const int MaxCached = 64;

    private readonly SettingsService _settings;
    private readonly SpeechPlayer _player;
    private readonly HotkeysViewModel _hotkeys;
    private readonly Dictionary<string, SpokenPhrase> _cache = [];
    private int _requests;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SayCommand), nameof(SavePhraseCommand))]
    private string _text = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public SpeechViewModel(SettingsService settings, SpeechPlayer player, HotkeysViewModel hotkeys)
    {
        _settings = settings;
        _player = player;
        _hotkeys = hotkeys;
        Voices = SpeechService.GetVoices();
        _player.Volume = (float)settings.Current.SpeechVolume;

        foreach (var entry in settings.Current.Phrases) Phrases.Add(CreateItem(entry));
        Phrases.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPhrases));
    }

    /// <summary>Ha cambiado «Con la voz que tengas puesta»: la app vuelve a mandar la voz a las frases.</summary>
    public event EventHandler? WithVoiceChanged;

    public IReadOnlyList<SpeechVoiceInfo> Voices { get; }

    public bool HasVoices => Voices.Count > 0;

    public ObservableCollection<PhraseItemViewModel> Phrases { get; } = [];

    public bool HasPhrases => Phrases.Count > 0;

    public int MaxLength => SpeechService.MaxLength;

    private AppSettings S => _settings.Current;

    public SpeechVoiceInfo? SelectedVoice
    {
        get => Voices.FirstOrDefault(v => v.Id == S.SpeechVoiceId) ?? Voices.FirstOrDefault();
        set
        {
            if (value is null || value.Id == S.SpeechVoiceId) return;
            S.SpeechVoiceId = value.Id;
            _settings.ScheduleSave();
            OnPropertyChanged();
            PrepareAgain();
        }
    }

    public double Rate
    {
        get => S.SpeechRate;
        set
        {
            value = Math.Clamp(Math.Round(value, 1), 0.5, 2);
            if (value == S.SpeechRate) return;
            S.SpeechRate = value;
            _settings.ScheduleSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RateText));
            PrepareAgain();
        }
    }

    public string RateText => $"{Rate:0.0}×";

    /// <summary>Volumen de las frases (0..2).</summary>
    public double Volume
    {
        get => S.SpeechVolume;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0, 2);
            if (value == S.SpeechVolume) return;
            S.SpeechVolume = value;
            _player.Volume = (float)value;
            _settings.ScheduleSave();
            OnPropertyChanged();
        }
    }

    /// <summary>Las frases pasan por la voz que tengas puesta (si no, suena la voz de Windows tal cual).</summary>
    public bool WithVoice
    {
        get => S.SpeechWithVoice;
        set
        {
            if (value == S.SpeechWithVoice) return;
            S.SpeechWithVoice = value;
            _settings.ScheduleSave();
            OnPropertyChanged();
            WithVoiceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(HasText))]
    private Task Say() => SayAsync(Text);

    [RelayCommand]
    public void Stop() => _player.Stop();

    [RelayCommand(CanExecute = nameof(HasText))]
    private void SavePhrase()
    {
        string text = Clean(Text);
        if (text.Length == 0) return;
        if (Phrases.Any(p => p.Text == text))
        {
            Error = Loc.T("Esa frase ya está guardada.");
            return;
        }
        var entry = new PhraseEntry { Text = text };
        S.Phrases.Add(entry);
        _settings.ScheduleSave();
        Phrases.Add(CreateItem(entry));
        Error = null;
        _ = PrepareAsync(text);
    }

    private bool HasText() => !string.IsNullOrWhiteSpace(Text);

    public void PlayById(string phraseId)
    {
        var item = Phrases.FirstOrDefault(p => p.Id == phraseId);
        if (item is not null) _ = SayAsync(item.Text);
    }

    public async Task SayAsync(string text)
    {
        text = Clean(text);
        if (text.Length == 0) return;
        if (!HasVoices)
        {
            Error = Loc.T("Este Windows no tiene voces de texto a voz.");
            return;
        }

        int request = ++_requests;
        IsBusy = true;
        Error = null;
        try
        {
            var phrase = await GetAsync(text);
            // Si mientras se sintetizaba se pidió otra frase, esta ya no suena.
            if (request == _requests) _player.Play(phrase.Clip, phrase.PitchHz);
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo sintetizar «{text}»", ex);
            Error = Loc.F("No se pudo decir la frase: {0}", ex.Message);
        }
        finally
        {
            if (request == _requests) IsBusy = false;
        }
    }

    /// <summary>Sintetiza las frases guardadas en segundo plano, para que sus atajos suenen al instante.</summary>
    public async Task PrepareSavedPhrasesAsync()
    {
        if (!HasVoices) return;
        foreach (var item in Phrases.ToList())
        {
            if (!await PrepareAsync(item.Text)) return;
        }
    }

    public void Delete(PhraseItemViewModel item)
    {
        if (!Dialogs.Confirm(Loc.F("¿Borrar la frase «{0}»?", item.Text))) return;
        _hotkeys.RemovePhraseBinding(item.Hotkey);
        Phrases.Remove(item);
        S.Phrases.Remove(item.Entry);
        _settings.ScheduleSave();
    }

    private void PrepareAgain()
    {
        _cache.Clear();
        _ = PrepareSavedPhrasesAsync();
    }

    private async Task<bool> PrepareAsync(string text)
    {
        try
        {
            await GetAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo preparar la frase «{text}»", ex);
            return false;
        }
    }

    private async Task<SpokenPhrase> GetAsync(string text)
    {
        string? voiceId = SelectedVoice?.Id;
        double rate = Rate;
        string key = $"{voiceId}|{rate}|{text}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var phrase = await SpeechService.SynthesizeAsync(text, voiceId, rate);
        if (_cache.Count >= MaxCached) _cache.Clear();
        _cache[key] = phrase;
        return phrase;
    }

    private PhraseItemViewModel CreateItem(PhraseEntry entry)
    {
        HotkeyGesture? gesture = HotkeyGesture.TryParse(entry.Hotkey, out var parsed) ? parsed : null;
        var binding = new HotkeyBindingViewModel(_hotkeys, HotkeyActions.PhraseAction(entry.Id), Shorten(entry.Text), gesture);
        binding.GestureChanged += (_, _) =>
        {
            entry.Hotkey = binding.Gesture?.ToString();
            _settings.ScheduleSave();
        };
        _hotkeys.AddPhraseBinding(binding);
        return new PhraseItemViewModel(this, entry, binding);
    }

    /// <summary>Una sola línea, sin espacios repetidos y como mucho <see cref="SpeechService.MaxLength"/> caracteres.</summary>
    private static string Clean(string text)
    {
        string single = WhiteSpace().Replace(text ?? "", " ").Trim();
        return single.Length <= SpeechService.MaxLength ? single : single[..SpeechService.MaxLength];
    }

    private static string Shorten(string text) => text.Length <= 50 ? text : text[..47] + "…";

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
