using M0DV0IC3.Dsp.Effects;

namespace M0DV0IC3.Dsp.Pitch;

/// <summary>
/// Cambio de tono TD-PSOLA en streaming, pensado para la mínima latencia.
/// <list type="bullet">
/// <item>YIN estima el periodo y se colocan marcas de análisis, una por periodo: la primera de cada tramo sonoro en
/// el pico del pulso glotal y las siguientes donde el ciclo más se parece al anterior, para que caigan siempre en
/// el mismo punto del ciclo.</item>
/// <item>Las marcas de síntesis se separan el periodo de cada ciclo / ratio (o 1/robotHz en modo robot), así se
/// conserva la pequeña irregularidad natural entre ciclos. En cada una se suma un grano
/// con ventana Hann tomado de la <b>marca de análisis válida más reciente</b>, es decir, la más nueva cuyo grano
/// ya tenemos entero. Así el retardo es el mínimo posible: semiancho de síntesis + semiancho de análisis,
/// unos 2 periodos de la voz y no 2 periodos del tono más grave admitido.</item>
/// <item>El grano se remuestrea por <c>formantRatio</c>: los formantes se mueven por separado del tono, que es lo
/// que hace falta para una voz de mujer u hombre creíble.</item>
/// <item>En los tramos sordos (s, f, ch, silencio) se hace overlap-add de ruido al 50 %, sin cambiar el tono.</item>
/// <item>El autotune (<see cref="PitchCorrector"/>) y el vibrato solo cambian la separación de las marcas de
/// síntesis, así que no añaden latencia.</item>
/// </list>
/// El bloque de entrada se procesa en sub-bloques de 32 muestras, para que el instante de colocación de
/// los granos no dependa del tamaño de paquete de WASAPI.
/// </summary>
public sealed class PsolaPitchShifter : IAudioEffect
{
    private const int RingSize = 1 << 14;
    private const int RingMask = RingSize - 1;
    private const int MarkCapacity = 1 << 10;
    private const int MarkMask = MarkCapacity - 1;
    private const int SubBlock = 32;
    private const int HannResolution = 1024;
    private const int UnvoicedHalfLength = 240;
    private const float SilenceLevel = 0.00316f;

    private readonly float[] _input = new float[RingSize];
    private readonly float[] _lowPassed = new float[RingSize];
    private readonly float[] _output = new float[RingSize];
    private readonly long[] _markPos = new long[MarkCapacity];
    private readonly float[] _markPeriod = new float[MarkCapacity];
    private readonly bool[] _markVoiced = new bool[MarkCapacity];
    private readonly float[] _hann = new float[HannResolution + 2];
    private readonly YinPitchDetector _yin;
    private readonly PitchCorrector _corrector;
    private readonly PitchProfile _profile;
    private readonly int _sampleRate;
    private readonly int _minPeriod;
    private readonly int _maxPeriod;

    private long _inPos;
    private long _markCount;
    private long _markCursor;
    private long _lastMarkPos;
    private double _nextSynth;
    private double _latency;
    private double _heldDelay;
    private int _silentSamples;

    private double _pitchRatio = 1.0;
    private double _transposeSemitones;
    private double _formantRatio = 1.0;
    private double _robotHz;
    private double _targetHz;
    private double _minShift = -12;
    private double _maxShift = 24;
    private double _effectiveShift = double.NaN;
    private double _vibratoDepth;
    private WobbleLfo _vibrato;
    private double _previousHop;

    public PsolaPitchShifter(int sampleRate, VoiceRange range = VoiceRange.Medium, PitchProfile? profile = null)
    {
        _sampleRate = sampleRate;
        _profile = profile ?? new PitchProfile();
        _minPeriod = (int)(sampleRate / range.MaxFrequency());
        _maxPeriod = (int)Math.Ceiling(sampleRate / range.MinFrequency());
        _yin = new YinPitchDetector(sampleRate, range.MinFrequency(), range.MaxFrequency());
        _corrector = new PitchCorrector(sampleRate);
        for (int i = 0; i <= HannResolution; i++)
            _hann[i] = (float)(0.5 * (1.0 + Math.Cos(Math.PI * i / HannResolution)));
        Reset();
    }

    public double PitchRatio => _pitchRatio;

    public double FormantRatio => _formantRatio;

    public double RobotHz => _robotHz;

    /// <summary>Último periodo detectado de la voz de entrada, en muestras (para la UI y los tests).</summary>
    public double DetectedPeriod => _yin.PeriodSamples;

    public bool IsVoiced => _yin.IsVoiced;

    /// <summary>Nota MIDI a la que el autotune lleva la voz ahora, o -1 (sin autotune o sin voz).</summary>
    public int AutotuneNote => _corrector.IsActive ? _corrector.CurrentNote : -1;

    public int LatencySamples => (int)_latency;

    /// <summary>Cambio de tono que se está aplicando ahora, en semitonos (con tono objetivo, el que se ha calculado).</summary>
    public double EffectiveSemitones => double.IsNaN(_effectiveShift) ? _transposeSemitones : _effectiveShift;

    public PitchProfile Profile => _profile;

    /// <summary>Cambia los parámetros. Se llama desde el hilo de audio y afecta a partir del siguiente grano.</summary>
    public void SetParameters(double pitchRatio, double formantRatio, double robotHz)
    {
        _pitchRatio = Math.Clamp(pitchRatio, 0.25, 4.0);
        _transposeSemitones = 12 * Math.Log2(_pitchRatio);
        // Con tono objetivo, el signo del cambio fijo dice hacia dónde va la voz: una voz para subir nunca baja
        // el tono (si ya hablas así de agudo, sube al menos 2 semitonos) y al revés.
        (_minShift, _maxShift) = _transposeSemitones switch
        {
            > 0.01 => (2.0, 24.0),
            < -0.01 => (-12.0, -2.0),
            _ => (-12.0, 24.0),
        };
        _formantRatio = Math.Clamp(formantRatio, 0.5, 2.0);
        _robotHz = robotHz > 0 ? Math.Clamp(robotHz, 40.0, 1000.0) : 0.0;
    }

    /// <summary>
    /// Autotune: con una escala distinta de <see cref="AutotuneScale.Off"/>, cada grano se lleva a la nota más cercana
    /// de la escala (tras aplicar el cambio de tono fijo). El modo robot tiene prioridad.
    /// </summary>
    public void SetAutotune(AutotuneScale scale, int key, double retuneMs, double exaggeration = 0) =>
        _corrector.Configure(scale, key, retuneMs, exaggeration);

    /// <summary>Vibrato (algo irregular) en el tono de salida: <paramref name="depthSemitones"/> de desviación máxima.</summary>
    /// <summary>
    /// Tono objetivo: en vez de un cambio fijo, el tono medio de la voz (<see cref="PitchProfile"/>) se lleva a
    /// <paramref name="targetHz"/>, conservando la entonación. 0 lo apaga. Hasta aprender el tono medio se usa el
    /// cambio fijo de <see cref="SetParameters"/>.
    /// </summary>
    public void SetTargetPitch(double targetHz) => _targetHz = targetHz > 0 ? Math.Clamp(targetHz, 40, 1000) : 0;

    public void SetVibrato(double rateHz, double depthSemitones)
    {
        _vibratoDepth = rateHz > 0 ? Math.Clamp(depthSemitones, 0, 3) : 0;
        _vibrato.SetRate(Math.Clamp(rateHz, 0, 20), _sampleRate);
    }

    public void Reset()
    {
        Array.Clear(_input);
        Array.Clear(_lowPassed);
        Array.Clear(_output);
        _yin.Reset();
        _inPos = 0;
        _markCount = 0;
        _markCursor = 0;
        _lastMarkPos = -_maxPeriod;
        _nextSynth = 0;
        _latency = 2.0 * _sampleRate / 150.0;
        _heldDelay = 0;
        _silentSamples = 0;
        _previousHop = 0;
        _effectiveShift = double.NaN;
        _corrector.Reset();
        _vibrato.Reset();
    }

    public void Process(Span<float> buffer)
    {
        for (int offset = 0; offset < buffer.Length; offset += SubBlock)
            ProcessSubBlock(buffer.Slice(offset, Math.Min(SubBlock, buffer.Length - offset)));
    }

    private void ProcessSubBlock(Span<float> block)
    {
        long blockStart = _inPos;
        float peak = 0f;
        for (int i = 0; i < block.Length; i++)
        {
            int idx = (int)((blockStart + i) & RingMask);
            float x = block[i];
            _input[idx] = x;
            _lowPassed[idx] = _yin.Push(x);
            peak = Math.Max(peak, Math.Abs(x));
        }
        _inPos += block.Length;

        // En silencio (más de 150 ms por debajo de -50 dBFS) el retardo vuelve poco a poco al mínimo:
        // comprimir silencio un 5 % no se oye y la siguiente frase arranca con menos latencia.
        _silentSamples = peak < SilenceLevel ? _silentSamples + block.Length : 0;
        if (_silentSamples > _sampleRate * 0.15) _heldDelay = Math.Max(0, _heldDelay - 0.05 * block.Length);
        long lastAvailable = _inPos - 1;

        PlaceAnalysisMarks(lastAvailable);
        PlaceGrains(blockStart, lastAvailable);

        for (int i = 0; i < block.Length; i++)
        {
            int idx = (int)((blockStart + i) & RingMask);
            block[i] = _output[idx];
            _output[idx] = 0f;
        }
    }

    private void PlaceAnalysisMarks(long lastAvailable)
    {
        bool voiced = _yin.IsVoiced;
        double period = voiced ? Math.Clamp(_yin.PeriodSamples, _minPeriod, _maxPeriod) : UnvoicedHalfLength;
        int step = (int)Math.Round(period);
        int radius = voiced ? step / 3 : 0;

        // Si las marcas se han quedado muy atrás (inicio o salto), se recolocan cerca del presente.
        if (_lastMarkPos < lastAvailable - 4L * _maxPeriod) _lastMarkPos = lastAvailable - 2L * _maxPeriod;

        while (true)
        {
            long candidate = _lastMarkPos + step;
            // La alineación compara medio periodo a cada lado de la marca: hace falta ese margen de señal.
            int reach = voiced ? radius + step / 2 : 0;
            if (candidate + reach > lastAvailable) break;

            long pos = candidate;
            if (voiced)
            {
                bool previousVoiced = _markCount > 0 && _markVoiced[(int)((_markCount - 1) & MarkMask)];
                pos = previousVoiced ? AlignWithPrevious(candidate, radius, step) : PeakNear(candidate, radius);
                pos = Math.Max(pos, _lastMarkPos + step / 2);
            }

            int m = (int)(_markCount & MarkMask);
            _markPos[m] = pos;
            _markPeriod[m] = (float)period;
            _markVoiced[m] = voiced;
            _markCount++;
            _lastMarkPos = pos;

            // El cursor nunca debe apuntar a una marca ya sobrescrita en el anillo.
            if (_markCount - _markCursor >= MarkCapacity) _markCursor = _markCount - MarkCapacity / 2;
        }
    }

    private long PeakNear(long candidate, int radius)
    {
        long pos = candidate;
        float best = float.MinValue;
        for (long k = candidate - radius; k <= candidate + radius; k++)
        {
            float v = _lowPassed[(int)(k & RingMask)];
            if (v > best) { best = v; pos = k; }
        }
        return pos;
    }

    /// <summary>
    /// Coloca la marca donde el ciclo se parece más al anterior (correlación normalizada de un periodo de
    /// señal filtrada). Así todas las marcas caen en el mismo punto del ciclo aunque haya varios picos
    /// parecidos, y la separación entre marcas es el periodo real de cada ciclo. Con picos a ojo, la marca
    /// saltaba de un pico a otro: eso añadía jitter y la voz grave sonaba ronca y metálica.
    /// </summary>
    private long AlignWithPrevious(long candidate, int radius, int step)
    {
        // Primero cada 2 posiciones con una muestra de cada 4 (la señal está filtrada a 1,8 kHz: 12 kHz bastan) y
        // luego se afina alrededor de la mejor. Cuesta la cuarta parte que probarlas todas, con el mismo resultado.
        long pos = BestAlignment(candidate - radius, candidate + radius, 2, step / 2, 4);
        return BestAlignment(pos - 1, pos + 1, 1, step / 2, 2);
    }

    private long BestAlignment(long from, long to, int stride, int half, int sampleStride)
    {
        long previous = _lastMarkPos;
        long pos = from;
        double best = double.MinValue;
        for (long k = from; k <= to; k += stride)
        {
            double cross = 0, energy = 1e-12;
            for (int j = -half; j <= half; j += sampleStride)
            {
                float a = _lowPassed[(int)((previous + j) & RingMask)];
                float b = _lowPassed[(int)((k + j) & RingMask)];
                cross += a * b;
                energy += b * b;
            }
            double score = cross / Math.Sqrt(energy);
            if (score > best) { best = score; pos = k; }
        }
        return pos;
    }

    private void PlaceGrains(long blockStart, long lastAvailable)
    {
        if (_markCount == 0) return;
        double f = _formantRatio;

        while (true)
        {
            // Avanza hasta la marca válida más reciente: la más nueva cuyo grano ya tenemos entero.
            while (_markCursor + 1 < _markCount)
            {
                int next = (int)((_markCursor + 1) & MarkMask);
                if (_markPos[next] + AnalysisHalf(_markPeriod[next], _markVoiced[next], f) <= lastAvailable) _markCursor++;
                else break;
            }

            int mi = (int)(_markCursor & MarkMask);
            bool voiced = _markVoiced[mi];
            double basePeriod = voiced ? _markPeriod[mi] : UnvoicedHalfLength;
            double synthHalf = f >= 1.0 ? basePeriod / f : basePeriod;
            double analysisHalf = synthHalf * f;

            if (voiced && _markPos[mi] + analysisHalf > lastAvailable) return;
            if (_nextSynth - synthHalf > lastAvailable) return;
            if (_nextSynth < blockStart) _nextSynth = blockStart;

            double hop;
            double center;
            float gain;
            if (voiced)
            {
                double vibrato = _vibratoDepth > 0 ? DspMath.SemitonesToRatio(_vibratoDepth * _vibrato.Value) : 1.0;
                double inputHz = _sampleRate / basePeriod;
                _profile.Add(inputHz, _previousHop / _sampleRate);
                double shift = NextShift(_previousHop);
                if (_robotHz > 0) hop = _sampleRate / (_robotHz * vibrato);
                else if (_corrector.IsActive)
                    hop = basePeriod / (_corrector.NextRatio(inputHz, shift, _previousHop) * vibrato);
                else
                {
                    // Cambio de tono normal: cada ciclo de salida dura lo que su ciclo de entrada / ratio, así se
                    // conservan las pequeñas variaciones naturales de la voz. Con el periodo medio de YIN todos los
                    // ciclos salían idénticos, y una voz sin esas variaciones suena a sintetizador.
                    hop = LocalPeriod(mi, basePeriod) / (DspMath.SemitonesToRatio(shift) * vibrato);
                }
                center = _markPos[mi];
                // Con mucho solapamiento (subir tono) los granos se suman coherentemente: se compensa el nivel.
                gain = (float)(1.0 / Math.Sqrt(Math.Max(1.0, synthHalf / hop)));
            }
            else
            {
                // Ruido: Hann al 50 % suma exactamente 1. Se mantiene el retardo de los tramos sonoros recientes
                // (sin bajar del mínimo causal): si no, cada paso sordo↔sonoro saltaría ~1 periodo y repetiría o
                // saltaría audio en el arranque de las vocales.
                hop = synthHalf;
                center = _nextSynth - Math.Max(synthHalf + analysisHalf, _heldDelay);
                gain = 1f;
                _corrector.Release();
            }

            AddGrain(_nextSynth, center, synthHalf, f, gain, blockStart);
            double delay = _nextSynth - center;
            if (voiced) _heldDelay += 0.2 * (delay - _heldDelay);
            _latency += 0.05 * (delay - _latency);
            hop = Math.Max(hop, 8.0);
            _nextSynth += hop;
            _previousHop = hop;
            _vibrato.Advance(hop);
        }
    }

    /// <summary>
    /// Cambio de tono para el siguiente grano. Con tono objetivo sale del tono medio aprendido y cambia con
    /// suavidad (~0,3 s), para que al aprenderlo o al moverse la media no haya saltos.
    /// </summary>
    private double NextShift(double elapsedSamples)
    {
        double target = _transposeSemitones;
        if (_targetHz > 0 && _profile.IsLearned)
            target = Math.Clamp(12 * Math.Log2(_targetHz / _profile.CenterHz), _minShift, _maxShift);

        if (double.IsNaN(_effectiveShift)) _effectiveShift = target;
        double seconds = elapsedSamples / _sampleRate;
        _effectiveShift += seconds / (0.3 + seconds) * (target - _effectiveShift);
        return _effectiveShift;
    }

    /// <summary>Distancia a la marca anterior si las dos son sonoras y es creíble (±25 % del periodo medio).</summary>
    private double LocalPeriod(int mark, double averagePeriod)
    {
        int previous = (mark - 1) & MarkMask;
        if (!_markVoiced[previous]) return averagePeriod;
        double local = _markPos[mark] - _markPos[previous];
        return local > 0.8 * averagePeriod && local < 1.25 * averagePeriod ? local : averagePeriod;
    }

    private static double AnalysisHalf(float period, bool voiced, double formant)
    {
        double basePeriod = voiced ? period : UnvoicedHalfLength;
        return formant >= 1.0 ? basePeriod : basePeriod * formant;
    }

    private void AddGrain(double synthCenter, double analysisCenter, double synthHalf, double formant, float gain, long firstWritable)
    {
        long s = (long)Math.Round(synthCenter);
        int half = (int)synthHalf;
        if (half < 2) return;
        long start = Math.Max(s - half, firstWritable);
        long end = s + half;
        double scale = HannResolution / synthHalf;

        for (long t = start; t <= end; t++)
        {
            long offset = t - s;
            int w = (int)(Math.Abs(offset) * scale);
            if (w >= HannResolution) continue;

            double src = analysisCenter + offset * formant;
            long i0 = (long)Math.Floor(src);
            float frac = (float)(src - i0);
            float a = _input[(int)(i0 & RingMask)];
            float b = _input[(int)((i0 + 1) & RingMask)];
            _output[(int)(t & RingMask)] += gain * _hann[w] * (a + (b - a) * frac);
        }
    }
}
