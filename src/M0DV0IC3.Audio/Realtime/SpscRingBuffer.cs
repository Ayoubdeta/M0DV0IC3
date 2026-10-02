using System.Diagnostics;

namespace M0DV0IC3.Audio.Realtime;

/// <summary>
/// Buffer circular de floats sin bloqueos, para un único productor (hilo de captura) y un único
/// consumidor (hilo de salida). Toda la memoria se reserva en el constructor.
/// Además apunta cuándo llegó cada bloque, para medir cuánto espera de verdad cada muestra.
/// </summary>
public sealed class SpscRingBuffer
{
    private const int ArrivalCapacity = 256;
    private const int ArrivalMask = ArrivalCapacity - 1;

    private readonly float[] _buffer;
    private readonly int _mask;
    private readonly long[] _arrivalEnd = new long[ArrivalCapacity];
    private readonly long[] _arrivalTime = new long[ArrivalCapacity];
    private long _write;
    private long _read;
    private long _arrivalsWritten;
    private long _arrivalsRead;

    public SpscRingBuffer(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("La capacidad debe ser potencia de 2", nameof(capacity));
        _buffer = new float[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    /// <summary>Muestras listas para leer.</summary>
    public int Available => (int)(Volatile.Read(ref _write) - Volatile.Read(ref _read));

    /// <summary>Consumidor: posición absoluta de la próxima muestra que se leerá.</summary>
    public long ReadPosition => _read;

    /// <summary>Productor: escribe lo que quepa y devuelve cuántas muestras ha escrito (si está lleno, descarta el resto).</summary>
    public int Write(ReadOnlySpan<float> data) => Write(data, Stopwatch.GetTimestamp());

    /// <param name="arrivalTimestamp">Momento de llegada en ticks de <see cref="Stopwatch"/> (los tests lo simulan).</param>
    public int Write(ReadOnlySpan<float> data, long arrivalTimestamp)
    {
        long write = _write;
        int free = _buffer.Length - (int)(write - Volatile.Read(ref _read));
        int count = Math.Min(free, data.Length);
        if (count <= 0) return 0;

        int start = (int)(write & _mask);
        int first = Math.Min(count, _buffer.Length - start);
        data[..first].CopyTo(_buffer.AsSpan(start));
        data.Slice(first, count - first).CopyTo(_buffer);

        // El registro de llegada se publica antes que los datos: cuando el consumidor ve una muestra, ya ve su hora.
        int slot = (int)(_arrivalsWritten & ArrivalMask);
        _arrivalEnd[slot] = write + count;
        _arrivalTime[slot] = arrivalTimestamp;
        Volatile.Write(ref _arrivalsWritten, _arrivalsWritten + 1);

        Volatile.Write(ref _write, write + count);
        return count;
    }

    /// <summary>Consumidor: lee hasta llenar <paramref name="destination"/> o vaciar el buffer.</summary>
    public int Read(Span<float> destination)
    {
        int count = Math.Min(Available, destination.Length);
        if (count <= 0) return 0;

        int start = (int)(_read & _mask);
        int first = Math.Min(count, _buffer.Length - start);
        _buffer.AsSpan(start, first).CopyTo(destination);
        _buffer.AsSpan(0, count - first).CopyTo(destination[first..]);
        Volatile.Write(ref _read, _read + count);
        return count;
    }

    /// <summary>Consumidor: mira una muestra sin consumirla (offset relativo a la posición de lectura, menor que <see cref="Available"/>).</summary>
    public float PeekAt(int offset) => _buffer[(int)((_read + offset) & _mask)];

    /// <summary>Consumidor: da por leídas <paramref name="count"/> muestras.</summary>
    public void Advance(int count)
    {
        if (count <= 0) return;
        Volatile.Write(ref _read, _read + Math.Min(count, Available));
    }

    /// <summary>
    /// Consumidor: suma (en ticks) de lo que han esperado las muestras [<paramref name="from"/>, from + count)
    /// desde que llegaron hasta <paramref name="now"/>. Las muestras sin registro de llegada no suman.
    /// </summary>
    public long SumWaitTicks(long from, int count, long now)
    {
        long written = Volatile.Read(ref _arrivalsWritten);
        if (written - _arrivalsRead > ArrivalCapacity) _arrivalsRead = written - ArrivalCapacity;

        long end = from + count;
        long position = from;
        long sum = 0;
        while (position < end && _arrivalsRead < written)
        {
            int slot = (int)(_arrivalsRead & ArrivalMask);
            long blockEnd = _arrivalEnd[slot];
            if (blockEnd <= position)
            {
                _arrivalsRead++;
                continue;
            }
            long upTo = Math.Min(blockEnd, end);
            sum += (upTo - position) * (now - _arrivalTime[slot]);
            position = upTo;
            if (blockEnd <= end) _arrivalsRead++;
        }
        return sum;
    }
}
