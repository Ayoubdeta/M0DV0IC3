using M0DV0IC3.Dsp;

namespace M0DV0IC3.Audio.NoiseSuppression;

/// <summary>
/// Supresión de ruido con RNNoise (red neuronal recurrente de Xiph/Mozilla).
/// <para>
/// RNNoise trabaja en tramas fijas de 480 muestras y tiene 10 ms de retardo propio (solape de ventanas).
/// Si los paquetes de WASAPI llegan de 480, que es lo normal en modo compartido a 10 ms, no se añade
/// nada más. Si llegan de otro tamaño, la primera vez se insertan los ceros justos para que nunca falte
/// salida, y ese colchón queda fijo.
/// </para>
/// </summary>
public sealed unsafe class RnNoiseEffect : IAudioEffect, IDisposable
{
    public const int FrameSize = 480;

    private const int FifoSize = 8192;
    private const int FifoMask = FifoSize - 1;
    private const float Scale = 32768f;

    private readonly float[] _frameIn = new float[FrameSize];
    private readonly float[] _frameOut = new float[FrameSize];
    private readonly float[] _fifo = new float[FifoSize];
    private IntPtr _state;
    private int _inCount;
    private long _fifoWrite;
    private long _fifoRead;
    private int _insertedLatency;
    private float _voiceProbability;

    private RnNoiseEffect(IntPtr state) => _state = state;

    /// <summary>Probabilidad de voz (0..1) de la última trama, según la propia red.</summary>
    public float VoiceProbability => Volatile.Read(ref _voiceProbability);

    public int LatencySamples => FrameSize + _insertedLatency;

    /// <summary>Carga la librería nativa. Devuelve null (con el motivo) si no está disponible.</summary>
    public static RnNoiseEffect? TryCreate(out string? error)
    {
        try
        {
            int frameSize = RnNoiseNative.GetFrameSize();
            if (frameSize != FrameSize)
            {
                error = $"rnnoise.dll usa tramas de {frameSize} muestras; se esperaban {FrameSize}.";
                return null;
            }
            IntPtr state = RnNoiseNative.Create(IntPtr.Zero);
            if (state == IntPtr.Zero)
            {
                error = "rnnoise_create devolvió null.";
                return null;
            }
            error = null;
            return new RnNoiseEffect(state);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = $"No se pudo cargar rnnoise.dll: {ex.Message}";
            return null;
        }
    }

    public void Process(Span<float> buffer)
    {
        if (_state == IntPtr.Zero) return;

        foreach (float x in buffer)
        {
            _frameIn[_inCount++] = x * Scale;
            if (_inCount == FrameSize)
            {
                _inCount = 0;
                fixed (float* input = _frameIn)
                fixed (float* output = _frameOut)
                {
                    Volatile.Write(ref _voiceProbability, RnNoiseNative.ProcessFrame(_state, output, input));
                }
                for (int k = 0; k < FrameSize; k++)
                    _fifo[(int)((_fifoWrite + k) & FifoMask)] = _frameOut[k] / Scale;
                _fifoWrite += FrameSize;
            }
        }

        int available = (int)(_fifoWrite - _fifoRead);
        int missing = Math.Max(0, buffer.Length - available);
        if (missing > 0)
        {
            buffer[..missing].Clear();
            _insertedLatency += missing;
        }
        for (int i = missing; i < buffer.Length; i++)
            buffer[i] = _fifo[(int)(_fifoRead++ & FifoMask)];
    }

    public void Reset()
    {
        _inCount = 0;
        _fifoRead = _fifoWrite = 0;
        _insertedLatency = 0;
        if (_state != IntPtr.Zero) RnNoiseNative.Init(_state, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_state == IntPtr.Zero) return;
        RnNoiseNative.Destroy(_state);
        _state = IntPtr.Zero;
    }
}
