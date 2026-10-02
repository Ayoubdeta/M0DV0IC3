namespace M0DV0IC3.Dsp.Effects;

/// <summary>
/// Voz invertida: trocea la voz en fragmentos y reproduce cada uno al revés.
/// <list type="bullet">
/// <item>Dos lectores, desfasados medio fragmento y con ventana sin², evitan los clics en los cortes:
/// sus ventanas suman exactamente 1.</item>
/// <item>No se puede invertir lo que aún no se ha dicho, así que el retardo medio es un fragmento entero.</item>
/// </list>
/// </summary>
public sealed class Reverse : IAudioEffect
{
    public const double MinFragmentMs = 60;
    public const double MaxFragmentMs = 500;

    private const int RingSize = 1 << 16;
    private const int RingMask = RingSize - 1;
    private const int TableSize = 2048;
    private const int FadeSamples = 240;

    private readonly float[] _ring = new float[RingSize];
    private readonly float[] _sin2 = new float[TableSize + 1];
    private readonly int _sampleRate;
    private long _pos;
    private int _fragment;
    private int _pendingFragment;
    private float _fade = 1f;
    private bool _started;

    public Reverse(int sampleRate)
    {
        _sampleRate = sampleRate;
        if (2 * MaxFragmentMs * sampleRate / 1000 >= RingSize) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        for (int i = 0; i <= TableSize; i++)
        {
            double s = Math.Sin(Math.PI * i / TableSize);
            _sin2[i] = (float)(s * s);
        }
    }

    public bool IsActive => _fragment > 0 || _pendingFragment > 0;

    public int LatencySamples => _pendingFragment;

    /// <param name="fragmentMs">Longitud de cada trozo invertido (0 = apagado). Es también el retardo que añade.</param>
    public void Configure(double fragmentMs)
    {
        int samples = fragmentMs > 0
            ? 2 * (int)Math.Round(Math.Clamp(fragmentMs, MinFragmentMs, MaxFragmentMs) * _sampleRate / 2000)
            : 0;
        _pendingFragment = samples;
        if (!_started) _fragment = samples;
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        _started = true;
        for (int i = 0; i < buffer.Length; i++)
        {
            long n = _pos++;
            _ring[(int)(n & RingMask)] = buffer[i];

            // Al cambiar la longitud, se baja a cero, se cambia y se vuelve a subir (5 ms): sin clics.
            if (_pendingFragment != _fragment)
            {
                _fade -= 1f / FadeSamples;
                if (_fade <= 0f)
                {
                    _fade = 0f;
                    _fragment = _pendingFragment;
                }
            }
            else if (_fade < 1f)
            {
                _fade = Math.Min(1f, _fade + 1f / FadeSamples);
            }

            int length = _fragment;
            if (length == 0)
            {
                buffer[i] = 0f;
                continue;
            }

            // Lector A: fragmentos que empiezan en múltiplos de la longitud; lector B, desfasado media longitud.
            // Cada uno reproduce al revés el fragmento anterior: la muestra n - 2j - 1.
            int jA = (int)(n % length);
            int jB = (int)((n + length / 2) % length);
            float a = _ring[(int)((n - 2L * jA - 1) & RingMask)];
            float b = _ring[(int)((n - 2L * jB - 1) & RingMask)];
            float wA = _sin2[(int)((long)jA * TableSize / length)];
            buffer[i] = _fade * (wA * a + (1f - wA) * b);
        }
    }

    public void Reset()
    {
        Array.Clear(_ring);
        _pos = 0;
        _fragment = _pendingFragment;
        _fade = 1f;
    }
}
