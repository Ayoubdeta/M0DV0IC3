using System.Windows.Input;
using M0DV0IC3.App.Services;

namespace M0DV0IC3.App.Models;

/// <summary>
/// Combinación de un atajo global. Se guarda como "Ctrl+Alt+Shift+Win+Tecla" (<see cref="ToString"/>)
/// y se muestra en español con <see cref="DisplayText"/> ("Ctrl+Mayús+←").
/// </summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public string DisplayText => string.Join("+", DisplayParts);

    /// <summary>Cada tecla por separado, para dibujarlas: ["Ctrl", "Alt", "V"], ["Num +"].</summary>
    public IReadOnlyList<string> DisplayParts => Parts(Modifiers, DisplayKeyName(Key), spanish: true);

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = ModifierKeys.None;
        var key = Key.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift" or "mayús" or "mayus":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (key != Key.None || !TryParseKey(part, out key)) return false;
                    break;
            }
        }

        if (key == Key.None || IsModifierKey(key)) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None
        or Key.ImeProcessed or Key.DeadCharProcessed;

    /// <summary>Teclas que se pueden usar solas sin estropear la escritura en otras apps.</summary>
    public static bool AllowsNoModifier(Key key) =>
        key is >= Key.F1 and <= Key.F24
        or >= Key.NumPad0 and <= Key.Divide
        or Key.Pause or Key.Scroll or Key.Insert
        or >= Key.MediaNextTrack and <= Key.MediaPlayPause;

    public override string ToString() => string.Join("+", Parts(Modifiers, KeyName(Key), spanish: false));

    private static List<string> Parts(ModifierKeys modifiers, string key, bool spanish)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(spanish ? "Mayús" : "Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key);
        return parts;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }
        if (text.Length == 4 && text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(text[3]))
        {
            key = Key.NumPad0 + (text[3] - '0');
            return true;
        }
        // Enum.TryParse también acepta números ("65"), que no son nombres de tecla.
        if (text.All(char.IsAsciiDigit)) return false;
        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        _ => key.ToString(),
    };

    private static string DisplayKeyName(Key key)
    {
        switch (key)
        {
            case >= Key.D0 and <= Key.D9:
                return KeyName(key);
            case >= Key.NumPad0 and <= Key.NumPad9:
                return "Num " + (key - Key.NumPad0);
            case >= Key.A and <= Key.Z:
            case >= Key.F1 and <= Key.F24:
                return key.ToString();
        }

        string? name = key switch
        {
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Space => "Espacio",
            Key.Enter => "Intro",
            Key.Back => "Retroceso",
            Key.Delete => "Supr",
            Key.Insert => "Insert",
            Key.Home => "Inicio",
            Key.End => "Fin",
            Key.PageUp => "RePág",
            Key.PageDown => "AvPág",
            Key.Tab => "Tab",
            Key.Pause => "Pausa",
            Key.Scroll => "Bloq Despl",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Divide => "Num /",
            Key.Decimal => "Num .",
            Key.MediaPlayPause => "Play/Pausa",
            Key.MediaNextTrack => "Siguiente pista",
            Key.MediaPreviousTrack => "Pista anterior",
            Key.MediaStop => "Stop",
            _ => null,
        };
        if (name is not null) return name;

        // Teclas OEM (º, ñ, +, ...): el carácter que imprime la tecla en la distribución actual.
        try
        {
            uint ch = NativeMethods.MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(key), NativeMethods.MapVkToChar) & 0x7FFF;
            if (ch > ' ') return char.ToUpperInvariant((char)ch).ToString();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return key.ToString();
    }
}
