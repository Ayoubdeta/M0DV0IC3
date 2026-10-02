namespace M0DV0IC3.Dsp;

/// <summary>
/// Efecto de audio mono en tiempo real.
/// <see cref="Process"/> trabaja in-place, se llama desde el hilo de audio y no debe reservar memoria.
/// </summary>
public interface IAudioEffect
{
    void Process(Span<float> buffer);

    void Reset();

    /// <summary>Retardo que introduce el efecto en este momento, en muestras.</summary>
    int LatencySamples { get; }
}
