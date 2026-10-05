namespace M0DV0IC3.App.Models;

/// <summary>Grupo del soundboard. <see cref="Accent"/> es una categoría de voces: da el color de la barra del título.</summary>
public sealed record SoundGroup(string Title, string Subtitle, int Order, string Accent);

/// <param name="Path">Ruta dentro de la carpeta Sonidos de la app, con «/».</param>
/// <param name="Since">Versión del catálogo en la que se añadió: los soundboards guardados reciben solo los nuevos.</param>
public sealed record BuiltInSound(string Path, string Name, SoundGroup Group, double Volume = 1.0, int Since = 1);

/// <summary>
/// Sonidos que trae la app, en la carpeta Sonidos junto al ejecutable. Son de Kenney (kenney.nl), con licencia
/// CC0 (dominio público): se pueden distribuir sin pedir permiso. Ver Sonidos\LICENCIA.txt.
/// </summary>
public static class BuiltInSounds
{
    /// <summary>Súbela al añadir sonidos al catálogo (con ese <see cref="BuiltInSound.Since"/>).</summary>
    public const int Version = 1;

    public static readonly SoundGroup Mine = new("Mis sonidos", "Los que añades tú: arrastra aquí archivos de audio", 0, nameof(VoiceCategory.Custom));
    public static readonly SoundGroup Radio = new("Radio", "Órdenes de radio en inglés, como en el Counter", 1, nameof(VoiceCategory.Autotune));
    public static readonly SoundGroup Announcer = new("Locutor", "El locutor de los juegos de lucha", 2, nameof(VoiceCategory.Characters));
    public static readonly SoundGroup Effects = new("Efectos", "Melodías y efectos cortos", 3, nameof(VoiceCategory.Effects));

    public static string Folder => System.IO.Path.Combine(AppContext.BaseDirectory, "Sonidos");

    public static IReadOnlyList<BuiltInSound> All { get; } =
    [
        new("radio/fire-in-the-hole.ogg", "Fire in the hole!", Radio),
        new("radio/go-go-go.ogg", "Go, go, go!", Radio),
        new("radio/cover-me.ogg", "Cover me!", Radio),
        new("radio/reloading.ogg", "Reloading!", Radio),
        new("radio/sniper.ogg", "Sniper!", Radio),
        new("radio/get-down.ogg", "Get down!", Radio),
        new("radio/look-out.ogg", "Look out!", Radio),
        new("radio/watch-my-back.ogg", "Watch my back!", Radio),
        new("radio/call-for-backup.ogg", "Backup!", Radio),
        new("radio/hold.ogg", "Hold!", Radio),
        new("radio/suppressing-fire.ogg", "Suppressing fire!", Radio),
        new("radio/target-destroyed.ogg", "Target destroyed", Radio),
        new("radio/medic.ogg", "Medic!", Radio),

        new("locutor/prepare-yourself.ogg", "Prepare yourself", Announcer),
        new("locutor/round-1.ogg", "Round 1", Announcer),
        new("locutor/fight.ogg", "Fight!", Announcer),
        new("locutor/final-round.ogg", "Final round", Announcer),
        new("locutor/sudden-death.ogg", "Sudden death", Announcer),
        new("locutor/multi-kill.ogg", "Multi kill!", Announcer),
        new("locutor/combo-breaker.ogg", "Combo breaker!", Announcer),
        new("locutor/flawless-victory.ogg", "Flawless victory", Announcer),
        new("locutor/winner.ogg", "Winner!", Announcer),
        new("locutor/loser.ogg", "Loser!", Announcer),
        new("locutor/game-over.ogg", "Game over", Announcer),
        new("locutor/mission-completed.ogg", "Mission completed", Announcer),
        new("locutor/mission-failed.ogg", "Mission failed", Announcer),

        new("efectos/victoria-8-bits.ogg", "Victoria (8 bits)", Effects),
        new("efectos/derrota-8-bits.ogg", "Derrota (8 bits)", Effects),
        new("efectos/saxo-triste.ogg", "Saxo triste", Effects, Volume: 1.6),
        new("efectos/golpe-dramatico.ogg", "Golpe dramático", Effects),
        new("efectos/correcto.ogg", "Correcto", Effects),
        new("efectos/incorrecto.ogg", "Incorrecto", Effects),
        new("efectos/power-up.ogg", "Power up", Effects),
        new("efectos/laser.ogg", "Láser", Effects, Volume: 0.5),
        new("efectos/explosion.ogg", "Explosión", Effects),
        new("efectos/campanazo.ogg", "Campanazo", Effects),
    ];

    public static BuiltInSound? Find(string? path) => path is null ? null : All.FirstOrDefault(s => s.Path == path);

    public static string FullPath(BuiltInSound sound) => System.IO.Path.Combine(Folder, sound.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public static SoundEntry CreateEntry(BuiltInSound sound) => new() { Name = sound.Name, BuiltIn = sound.Path, Volume = sound.Volume };
}
