using System.Runtime.InteropServices;
using System.Windows.Interop;
using M0DV0IC3.App.Localization;
using M0DV0IC3.App.Models;

namespace M0DV0IC3.App.Services;

/// <summary>
/// Atajos globales con RegisterHotKey sobre el HWND de la ventana principal (que sigue existiendo
/// aunque la ventana esté oculta en la bandeja). Úsalo solo desde el hilo de la UI.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<string, int> _idsByAction = [];
    private readonly Dictionary<int, string> _actionsById = [];
    private int _nextId = 1;

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("La ventana no tiene HwndSource.");
        _source.AddHook(WndProc);
    }

    /// <summary>Se ha pulsado el atajo de una acción (llega en el hilo de la UI).</summary>
    public event EventHandler<string>? Pressed;

    public bool Register(string actionId, HotkeyGesture gesture, out string? error)
    {
        Unregister(actionId);
        int id = _nextId;
        // Los id de aplicación van de 0x0000 a 0xBFFF.
        _nextId = _nextId >= 0xBFFF ? 1 : _nextId + 1;

        uint modifiers = (uint)gesture.Modifiers | NativeMethods.ModNoRepeat;
        if (!NativeMethods.RegisterHotKey(_hwnd, id, modifiers, gesture.VirtualKey))
        {
            int code = Marshal.GetLastPInvokeError();
            error = code == NativeMethods.ErrorHotkeyAlreadyRegistered
                ? Loc.F("{0} ya lo usa otra aplicación (o Windows). Elige otra combinación.", gesture.DisplayText)
                : Loc.F("No se pudo registrar {0} (error {1}).", gesture.DisplayText, code);
            return false;
        }

        _idsByAction[actionId] = id;
        _actionsById[id] = actionId;
        error = null;
        return true;
    }

    public void Unregister(string actionId)
    {
        if (!_idsByAction.Remove(actionId, out int id)) return;
        _actionsById.Remove(id);
        NativeMethods.UnregisterHotKey(_hwnd, id);
    }

    public void UnregisterAll()
    {
        foreach (int id in _actionsById.Keys) NativeMethods.UnregisterHotKey(_hwnd, id);
        _idsByAction.Clear();
        _actionsById.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _actionsById.TryGetValue(wParam.ToInt32(), out string? action))
        {
            handled = true;
            Pressed?.Invoke(this, action);
        }
        return IntPtr.Zero;
    }
}
