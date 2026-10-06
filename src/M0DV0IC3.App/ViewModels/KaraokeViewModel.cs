using System.Collections.ObjectModel;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M0DV0IC3.App.Services;
using M0DV0IC3.Audio.AppAudio;
using M0DV0IC3.Audio.Karaoke;

namespace M0DV0IC3.App.ViewModels;

/// <summary>Una línea de la letra en pantalla.</summary>
public sealed partial class LyricLineViewModel(LyricLine line) : ObservableObject
{
    public string Text { get; } = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;

    public TimeSpan Time { get; } = line.Time;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isPast;
}

/// <summary>
/// Pestaña Karaoke: la canción que suena en Spotify sin la voz, en tus cascos y en Discord, con su letra.
/// <list type="bullet">
/// <item>La canción y su posición salen de los controles multimedia de Windows (<see cref="NowPlayingService"/>).</item>
/// <item>La letra, de LRCLIB (<see cref="LrclibClient"/>); si está sincronizada, la línea que toca se ilumina.</item>
/// <item>La voz se quita al capturar Spotify (<see cref="M0DV0IC3.Dsp.Effects.VocalRemover"/>) y la música va
/// también a los auriculares. Spotify se baja en Windows para que no suene dos veces (con voz): <see cref="AppDucking"/>.</item>
/// </list>
/// </summary>
public sealed partial class KaraokeViewModel : ObservableObject, IDisposable
{
    private const string SpotifyExe = "Spotify";

    // La música llega a tus oídos unos 150 ms después de la posición que dice Spotify: captura (20 ms), margen
    // anti-cortes (12 ms), quitar la voz (43 ms), motor y salida a los cascos. La posición que da Spotify es fiable:
    // la actualiza cada ~4,5 s y, entre medias, contando el tiempo transcurrido no se desvía ni 10 ms.
    private static readonly TimeSpan AudioDelay = TimeSpan.FromMilliseconds(150);

    private readonly SettingsService _settings;
    private readonly AudioService _audio;
    private readonly MusicViewModel _music;
    private readonly VoicesViewModel _voices;
    private readonly Func<bool> _hasMonitor;
    private readonly NowPlayingService _nowPlaying = new();
    private readonly LrclibClient _lyricsClient;
    private readonly AppDucking _ducking;
    private readonly DispatcherTimer _songTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _lineTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private NowPlaying? _song;
    private NowPlaying? _lyricsSong;
    private SongLyrics? _lyrics;
    private CancellationTokenSource? _lyricsLoad;
    private bool _startedStreaming;
    private bool _busy;
    private bool _pageVisible;
    private bool _ticking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMonitorWarning))]
    private bool _isOn;

    [ObservableProperty] private string _songTitle = "";
    [ObservableProperty] private string _songArtist = "";
    [ObservableProperty] private bool _hasSong;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isSynced;
    [ObservableProperty] private int _currentIndex = -1;

    public KaraokeViewModel(SettingsService settings, AudioService audio, MusicViewModel music, VoicesViewModel voices, AppDucking ducking,
        Func<bool> hasMonitor)
    {
        _settings = settings;
        _audio = audio;
        _music = music;
        _ducking = ducking;
        _voices = voices;
        _hasMonitor = hasMonitor;
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1";
        _lyricsClient = new LrclibClient($"M0DV0IC3/{version} (+https://github.com/Ayoubdeta/M0DV0IC3)");
        _audio.AppAudio.VocalRemovalStrength = (float)settings.Current.KaraokeVocalStrength;

        _songTimer.Tick += async (_, _) => await OnSongTickAsync();
        _lineTimer.Tick += (_, _) => UpdateCurrentLine();
        _music.PropertyChanged += (_, e) =>
        {
            // Si apagas "Música por el micro" desde su pestaña, el karaoke se queda sin música: se apaga también.
            if (e.PropertyName == nameof(MusicViewModel.IsStreaming) && !_music.IsStreaming && IsOn && !_busy) IsOn = false;
        };
        _songTimer.Start();
    }

    public ObservableCollection<LyricLineViewModel> Lines { get; } = [];

    public bool IsSupported => AppAudioStreamer.IsSupported;

    public bool ShowMonitorWarning => IsOn && !_hasMonitor();

    /// <summary>Cuánta voz se quita (0..1).</summary>
    public double VocalStrength
    {
        get => _settings.Current.KaraokeVocalStrength;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0, 1);
            if (value == _settings.Current.KaraokeVocalStrength) return;
            _settings.Current.KaraokeVocalStrength = value;
            _settings.ScheduleSave();
            _audio.AppAudio.VocalRemovalStrength = (float)value;
            OnPropertyChanged();
        }
    }

    /// <summary>Volumen de la música (el mismo que en la pestaña Música).</summary>
    public double MusicVolumeDb
    {
        get => _music.VolumeDb;
        set
        {
            _music.VolumeDb = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Adelanta (positivo) o retrasa la letra respecto a la música, en segundos.</summary>
    public double LyricsOffset
    {
        get => _settings.Current.KaraokeLyricsOffset;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), -10, 10);
            if (value == _settings.Current.KaraokeLyricsOffset) return;
            _settings.Current.KaraokeLyricsOffset = value;
            _settings.ScheduleSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LyricsOffsetText));
        }
    }

    public string LyricsOffsetText => LyricsOffset == 0 ? "0 s" : $"{LyricsOffset:+0.00;-0.00} s";

    /// <summary>La pestaña está a la vista: hace falta la letra aunque el karaoke esté apagado.</summary>
    public void SetPageVisible(bool visible)
    {
        _pageVisible = visible;
        if (visible) _ = OnSongTickAsync();
    }

    [RelayCommand]
    private void Toggle() => IsOn = !IsOn;

    [RelayCommand]
    private void LyricsEarlier() => LyricsOffset += 0.25;

    [RelayCommand]
    private void LyricsLater() => LyricsOffset -= 0.25;

    /// <summary>Elige la voz "Autotune cantar": afinada para cantar encima de una canción.</summary>
    [RelayCommand]
    private void SingWithAutotune() => _voices.ActivateById("autotune-cantar");

    [RelayCommand]
    private Task Retry()
    {
        _lyricsSong = null;
        return OnSongTickAsync();
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_busy) return;
        _ = value ? StartAsync() : StopAsync();
    }

    private async Task StartAsync()
    {
        _busy = true;
        try
        {
            var app = AudioAppFinder.FindByExe(SpotifyExe);
            if (app is null)
            {
                Status = "Abre Spotify y pon una canción para usar el karaoke.";
                IsOn = false;
                return;
            }

            // Quitar la voz y cambiársela al cantante no van a la vez.
            await _music.StopSongVoiceAsync();
            bool wasStreamingSpotify = _music.IsStreaming
                && string.Equals(_audio.AppAudio.Current?.ExeName, SpotifyExe, StringComparison.OrdinalIgnoreCase);
            _music.KaraokeActive = true;
            _audio.AppAudio.Processing = SongProcessing.RemoveVocals;
            _audio.Pipeline.AppAudioToMonitor = true;
            if (!wasStreamingSpotify && !await _music.StreamAppAsync(SpotifyExe))
            {
                Status = _music.Message ?? "No se pudo capturar Spotify.";
                ResetAudio();
                IsOn = false;
                return;
            }
            _startedStreaming = !wasStreamingSpotify;
            await _ducking.DuckAsync(SpotifyExe);
            _lineTimer.Start();
            OnPropertyChanged(nameof(ShowMonitorWarning));
            await OnSongTickAsync();
            Log.Info("Karaoke: encendido");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task StopAsync()
    {
        _busy = true;
        try
        {
            _lineTimer.Stop();
            await _ducking.ReleaseAsync();
            ResetAudio();
            if (_startedStreaming && _music.IsStreaming) _music.IsStreaming = false;
            _startedStreaming = false;
            Log.Info("Karaoke: apagado");
        }
        finally
        {
            _busy = false;
        }
    }

    private void ResetAudio()
    {
        _audio.AppAudio.Processing = SongProcessing.None;
        _audio.Pipeline.AppAudioToMonitor = false;
        _music.KaraokeActive = false;
    }

    /// <summary>Al salir de la app (Spotify vuelve a su volumen con <see cref="AppDucking.Shutdown"/>).</summary>
    public void Shutdown()
    {
        _songTimer.Stop();
        _lineTimer.Stop();
    }

    public void Dispose()
    {
        _lyricsLoad?.Cancel();
        _lyricsClient.Dispose();
    }

    private async Task OnSongTickAsync()
    {
        // Buscar la letra puede tardar varios segundos: mientras, no se lanza otra consulta encima.
        if (_ticking) return;
        _ticking = true;
        try
        {
            await SongTickAsync();
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task SongTickAsync()
    {
        var song = await _nowPlaying.GetSpotifyAsync();
        _song = song;
        HasSong = song is not null;
        SongTitle = song?.Title ?? "";
        SongArtist = song?.Artist ?? "";

        if (IsOn && !_busy) OnPropertyChanged(nameof(ShowMonitorWarning));

        if (song is null)
        {
            Status = AudioAppFinder.FindByExe(SpotifyExe) is null
                ? "Abre Spotify y pon una canción."
                : "Pon una canción en Spotify.";
            return;
        }
        if ((IsOn || _pageVisible) && !song.IsSameSong(_lyricsSong)) await LoadLyricsAsync(song);
    }

    private async Task LoadLyricsAsync(NowPlaying song)
    {
        _lyricsLoad?.Cancel();
        var cancellation = _lyricsLoad = new CancellationTokenSource();
        _lyricsSong = song;
        _lyrics = null;
        Lines.Clear();
        CurrentIndex = -1;
        IsSynced = false;
        Status = "Buscando la letra…";
        try
        {
            var lyrics = await _lyricsClient.FindAsync(song.Artist, song.Title, song.Album, song.Duration, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            _lyrics = lyrics;
            if (lyrics is null)
            {
                Status = "No he encontrado la letra de esta canción.";
                return;
            }
            if (lyrics.IsInstrumental)
            {
                Status = "Esta canción es instrumental: no tiene letra.";
                return;
            }
            foreach (var line in lyrics.Lines) Lines.Add(new LyricLineViewModel(line));
            IsSynced = lyrics.IsSynced;
            Status = lyrics.IsSynced ? "" : "Letra sin sincronizar: síguela tú.";
            UpdateCurrentLine();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Warn("No se pudo descargar la letra", ex);
            _lyricsSong = null; // se reintenta en la siguiente consulta
            Status = "No se ha podido buscar la letra (¿hay conexión a internet?). Lo vuelvo a intentar en un momento.";
        }
    }

    private void UpdateCurrentLine()
    {
        if (_song is null || _lyrics is not { IsSynced: true } lyrics || Lines.Count != lyrics.Lines.Count) return;
        var position = _song.PositionNow(DateTimeOffset.Now) - AudioDelay + TimeSpan.FromSeconds(LyricsOffset);
        int index = lyrics.IndexAt(position);
        if (index == CurrentIndex) return;
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].IsCurrent = i == index;
            Lines[i].IsPast = i < index;
        }
        CurrentIndex = index;
    }
}
