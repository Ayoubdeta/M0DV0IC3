namespace M0DV0IC3.Audio.Realtime;

/// <summary>Pico máximo desde la última lectura. Lo escribe el hilo de audio y lo lee y reinicia la UI.</summary>
public sealed class PeakMeter
{
    private int _bits;

    public void Update(float peak)
    {
        int current = Volatile.Read(ref _bits);
        while (peak > BitConverter.Int32BitsToSingle(current))
        {
            int previous = Interlocked.CompareExchange(ref _bits, BitConverter.SingleToInt32Bits(peak), current);
            if (previous == current) break;
            current = previous;
        }
    }

    public float ReadAndReset() => BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _bits, 0));
}
