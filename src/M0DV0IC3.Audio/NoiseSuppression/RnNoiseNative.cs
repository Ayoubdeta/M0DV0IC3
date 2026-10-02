using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace M0DV0IC3.Audio.NoiseSuppression;

/// <summary>P/Invoke a rnnoise.dll (el del paquete YellowDogMan.RRNoise.NET, en runtimes/win-x64/native).</summary>
internal static unsafe partial class RnNoiseNative
{
    private const string Library = "rnnoise";

    /// <summary>Crea el estado del denoiser. <paramref name="model"/> = null usa el modelo integrado.</summary>
    [LibraryImport(Library, EntryPoint = "rnnoise_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr Create(IntPtr model);

    /// <summary>Reinicia un estado existente sin reservar memoria.</summary>
    [LibraryImport(Library, EntryPoint = "rnnoise_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Init(IntPtr state, IntPtr model);

    [LibraryImport(Library, EntryPoint = "rnnoise_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Destroy(IntPtr state);

    /// <summary>Procesa 480 muestras (10 ms a 48 kHz) en escala de 16 bits (±32768). Devuelve la probabilidad de voz.</summary>
    [LibraryImport(Library, EntryPoint = "rnnoise_process_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ProcessFrame(IntPtr state, float* output, float* input);

    [LibraryImport(Library, EntryPoint = "rnnoise_get_frame_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetFrameSize();
}
