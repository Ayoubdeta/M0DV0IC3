using System.Runtime.InteropServices;

namespace M0DV0IC3.App.Services;

internal static partial class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const uint ModNoRepeat = 0x4000;
    public const int ErrorHotkeyAlreadyRegistered = 1409;
    public const uint MapVkToChar = 2;

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1 o posterior).</summary>
    public const int DwmUseImmersiveDarkMode = 20;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    public static partial uint MapVirtualKey(uint code, uint mapType);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barra de título oscura, a juego con el tema.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows antiguo: se queda la barra clara.
        }
    }
}
