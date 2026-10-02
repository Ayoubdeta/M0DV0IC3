using System.Runtime.CompilerServices;

namespace M0DV0IC3.Dsp;

public static class DspMath
{
    /// <summary>Frecuencia de muestreo interna de todo el motor.</summary>
    public const int SampleRate = 48000;

    public static float DbToGain(double db) => (float)Math.Pow(10.0, db / 20.0);

    public static double GainToDb(double gain) => gain <= 1e-9 ? -180.0 : 20.0 * Math.Log10(gain);

    public static double SemitonesToRatio(double semitones) => Math.Pow(2.0, semitones / 12.0);

    /// <summary>Coeficiente de un filtro de un polo para una constante de tiempo dada.</summary>
    public static float TimeConstant(double milliseconds, int sampleRate) =>
        milliseconds <= 0 ? 0f : (float)Math.Exp(-1.0 / (milliseconds * 0.001 * sampleRate));

    /// <summary>
    /// .NET no activa flush-to-zero: los estados recursivos que decaen hacia cero acaban en
    /// números denormales, que son hasta 100 veces más lentos. Se limpian al final de cada bloque.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FlushDenormal(float x) => Math.Abs(x) < 1e-20f ? 0f : x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FlushDenormal(double x) => Math.Abs(x) < 1e-30 ? 0.0 : x;

    public static float Peak(ReadOnlySpan<float> buffer)
    {
        float peak = 0f;
        foreach (float x in buffer)
        {
            float a = Math.Abs(x);
            if (a > peak) peak = a;
        }
        return peak;
    }

    public static double Rms(ReadOnlySpan<float> buffer)
    {
        if (buffer.IsEmpty) return 0;
        double sum = 0;
        foreach (float x in buffer) sum += x * x;
        return Math.Sqrt(sum / buffer.Length);
    }
}
