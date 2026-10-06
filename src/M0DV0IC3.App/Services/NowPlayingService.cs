using Windows.Media.Control;

namespace M0DV0IC3.App.Services;

/// <summary>Canción que suena: lo que Spotify cuenta a Windows (los controles multimedia del sistema).</summary>
/// <param name="Position">Posición en el instante <paramref name="PositionUpdated"/>.</param>
public sealed record NowPlaying(
    string Title, string Artist, string Album, TimeSpan Duration, TimeSpan Position, DateTimeOffset PositionUpdated, bool IsPlaying)
{
    /// <summary>Misma canción (aunque cambie la posición o la pausa).</summary>
    public bool IsSameSong(NowPlaying? other) =>
        other is not null && other.Title == Title && other.Artist == Artist && Math.Abs((other.Duration - Duration).TotalSeconds) < 1;

    /// <summary>Posición ahora mismo: la última que dio Spotify más el tiempo que lleva sonando desde entonces.</summary>
    public TimeSpan PositionNow(DateTimeOffset now)
    {
        if (!IsPlaying) return Position;
        var position = Position + (now - PositionUpdated);
        return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
    }
}

/// <summary>
/// Lee la canción de Spotify con <c>GlobalSystemMediaTransportControlsSessionManager</c> (la misma información que
/// el panel multimedia de Windows). Funciona con la app de escritorio y con la de Microsoft Store. Si no hay
/// Spotify, devuelve null. Llámalo desde el hilo de la UI.
/// </summary>
public sealed class NowPlayingService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<NowPlaying?> GetSpotifyAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetSessions()
                .FirstOrDefault(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
            if (session is null) return null;

            var media = await session.TryGetMediaPropertiesAsync();
            if (media is null || string.IsNullOrWhiteSpace(media.Title)) return null;
            var timeline = session.GetTimelineProperties();
            bool playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new NowPlaying(media.Title.Trim(), (media.Artist ?? "").Trim(), (media.AlbumTitle ?? "").Trim(),
                timeline.EndTime - timeline.StartTime, timeline.Position, timeline.LastUpdatedTime, playing);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows puede fallar si Spotify se está cerrando o reiniciando: se vuelve a intentar en la siguiente consulta.
            Log.Warn("No se pudo leer la canción de Spotify", ex);
            _manager = null;
            return null;
        }
    }
}
