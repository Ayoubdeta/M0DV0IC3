namespace M0DV0IC3.Audio.Realtime;

/// <summary>
/// Cola de mensajes acotada para mandar órdenes al hilo de audio. Los productores se serializan
/// con un lock; el consumidor (hilo de audio) nunca bloquea ni reserva memoria.
/// </summary>
public sealed class SpscQueue<T>
{
    private readonly T[] _items;
    private readonly int _mask;
    private readonly Lock _producerLock = new();
    private long _head;
    private long _tail;

    public SpscQueue(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("La capacidad debe ser potencia de 2", nameof(capacity));
        _items = new T[capacity];
        _mask = capacity - 1;
    }

    public bool TryEnqueue(T item)
    {
        lock (_producerLock)
        {
            long tail = _tail;
            if (tail - Volatile.Read(ref _head) >= _items.Length) return false;
            _items[tail & _mask] = item;
            Volatile.Write(ref _tail, tail + 1);
            return true;
        }
    }

    public bool TryDequeue(out T item)
    {
        long head = _head;
        if (head >= Volatile.Read(ref _tail))
        {
            item = default!;
            return false;
        }
        int index = (int)(head & _mask);
        item = _items[index];
        _items[index] = default!;
        Volatile.Write(ref _head, head + 1);
        return true;
    }
}
