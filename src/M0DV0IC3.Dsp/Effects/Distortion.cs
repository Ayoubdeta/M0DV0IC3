namespace M0DV0IC3.Dsp.Effects;

/// <summary>Saturación suave con tanh. Con drive 0 no hace nada; con drive 1 distorsiona mucho (megáfono).</summary>
public sealed class Distortion : IAudioEffect
{
    private float _gain = 1f;
    private float _makeup = 1f;

    public bool IsActive { get; private set; }

    public int LatencySamples => 0;

    public void Configure(double drive)
    {
        double d = Math.Clamp(drive, 0, 1);
        IsActive = d > 0.001;
        _gain = (float)(1 + 30 * d);
        _makeup = 1f / MathF.Sqrt(_gain);
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = MathF.Tanh(_gain * buffer[i]) * _makeup;
    }

    public void Reset()
    {
    }
}
