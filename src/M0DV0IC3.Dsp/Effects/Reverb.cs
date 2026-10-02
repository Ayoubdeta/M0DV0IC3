namespace M0DV0IC3.Dsp.Effects;

/// <summary>Reverb Freeverb (Jezar at Dreampoint) en mono: 8 combs con amortiguación en paralelo y 4 all-pass en serie.</summary>
public sealed class Reverb : IAudioEffect
{
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllPassTunings = [556, 441, 341, 225];
    private const float FixedGain = 0.015f;
    private const float WetScale = 3f;

    private readonly float[][] _combs;
    private readonly int[] _combPos;
    private readonly float[] _combStore;
    private readonly float[][] _allPasses;
    private readonly int[] _allPassPos;

    private float _feedback = 0.84f;
    private float _damp1 = 0.2f;
    private float _damp2 = 0.8f;
    private float _wet;

    public Reverb(int sampleRate)
    {
        double scale = sampleRate / 44100.0;
        _combs = CombTunings.Select(t => new float[(int)(t * scale)]).ToArray();
        _allPasses = AllPassTunings.Select(t => new float[(int)(t * scale)]).ToArray();
        _combPos = new int[_combs.Length];
        _combStore = new float[_combs.Length];
        _allPassPos = new int[_allPasses.Length];
    }

    public bool IsActive => _wet > 0f;

    public int LatencySamples => 0;

    /// <param name="mix">0..1, cantidad de reverb.</param>
    /// <param name="roomSize">0..1, tamaño de la sala (duración de la cola).</param>
    /// <param name="damping">0..1, cuánto se apagan los agudos de la cola.</param>
    public void Configure(double mix, double roomSize, double damping = 0.5)
    {
        _wet = (float)Math.Clamp(mix, 0, 1);
        _feedback = (float)(Math.Clamp(roomSize, 0, 1) * 0.28 + 0.7);
        _damp1 = (float)(Math.Clamp(damping, 0, 1) * 0.4);
        _damp2 = 1f - _damp1;
    }

    public void Process(Span<float> buffer)
    {
        if (!IsActive) return;
        float dry = 1f - 0.5f * _wet;
        float wet = _wet * WetScale;

        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            float input = x * FixedGain;
            float sum = 0f;

            for (int c = 0; c < _combs.Length; c++)
            {
                float[] line = _combs[c];
                int p = _combPos[c];
                float output = line[p];
                _combStore[c] = output * _damp2 + _combStore[c] * _damp1;
                line[p] = input + _combStore[c] * _feedback;
                _combPos[c] = p + 1 == line.Length ? 0 : p + 1;
                sum += output;
            }

            for (int a = 0; a < _allPasses.Length; a++)
            {
                float[] line = _allPasses[a];
                int p = _allPassPos[a];
                float buffered = line[p];
                line[p] = sum + buffered * 0.5f;
                sum = buffered - sum;
                _allPassPos[a] = p + 1 == line.Length ? 0 : p + 1;
            }

            buffer[i] = dry * x + wet * sum;
        }

        for (int c = 0; c < _combStore.Length; c++) _combStore[c] = DspMath.FlushDenormal(_combStore[c]);
    }

    public void Reset()
    {
        foreach (var line in _combs) Array.Clear(line);
        foreach (var line in _allPasses) Array.Clear(line);
        Array.Clear(_combStore);
    }
}
