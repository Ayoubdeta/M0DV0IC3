using NAudio.CoreAudioApi;

namespace M0DV0IC3.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault)
{
    /// <summary>Endpoint de VB-Audio Virtual Cable (CABLE Input / CABLE Output y variantes A/B).</summary>
    public bool IsVirtualCable => DeviceService.IsVirtualCableName(Name);

    public override string ToString() => Name;
}

/// <summary>
/// Enumera los dispositivos de audio activos y avisa cuando cambian (se enchufa un micro, se instala VB-Cable...).
/// Créalo en el hilo de la UI: los avisos llegan por su SynchronizationContext.
/// </summary>
public sealed class DeviceService : IDisposable
{
    public const string CableDownloadUrl = "https://vb-audio.com/Cable/";

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;

    public DeviceService()
    {
        _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: true);
        _notifications.DeviceAdded += OnChanged;
        _notifications.DeviceRemoved += OnChanged;
        _notifications.DeviceStateChanged += OnChanged;
        _notifications.DefaultDeviceChanged += OnChanged;
    }

    public event EventHandler? DevicesChanged;

    public static bool IsVirtualCableName(string name) =>
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) && name.Contains("CABLE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Micrófonos reales. Se excluye CABLE Output: usarlo como entrada crearía un bucle de realimentación.</summary>
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        GetDevices(DataFlow.Capture).Where(d => !d.IsVirtualCable).ToList();

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => GetDevices(DataFlow.Render);

    /// <summary>Salida "CABLE Input", que es donde hay que mandar la voz para que Discord la vea en "CABLE Output".</summary>
    public AudioDeviceInfo? FindCableInput() =>
        GetOutputDevices().FirstOrDefault(d => d.IsVirtualCable && d.Name.Contains("Input", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<AudioDeviceInfo> GetDevices(DataFlow flow)
    {
        string? defaultId = null;
        if (_enumerator.TryGetDefaultAudioEndpoint(flow, Role.Console, out var defaultDevice))
        {
            defaultId = defaultDevice.ID;
            defaultDevice.Dispose();
        }

        var result = new List<AudioDeviceInfo>();
        using var collection = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var device in collection)
        {
            using (device)
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }
        return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Dispose()
    {
        _notifications.Dispose();
        _enumerator.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => DevicesChanged?.Invoke(this, EventArgs.Empty);
}
