using M0DV0IC3.Dsp;
using M0DV0IC3.Dsp.Pitch;

namespace M0DV0IC3.Cli.Commands;

internal static class AnalyzeCommand
{
    private const int ReportStep = DspMath.SampleRate / 10;  // 100 ms
    private const int StatsStep = DspMath.SampleRate / 100;  // 10 ms

    public static int Run(string[] args)
    {
        var a = CliArgs.Parse(args, ["range"], []);
        string path = a.Required(0, "el archivo a analizar");
        a.ExpectPositional(1);
        var range = a.GetRange();
        var x = AudioFiles.Load(path);

        Console.WriteLine($"Archivo: {path} ({AudioFiles.Seconds(x.Length):0.00} s, 48 kHz mono)");
        Console.WriteLine($"YIN, rango {CliArgs.RangeName(range)} ({range.MinFrequency():0}-{range.MaxFrequency():0} Hz)");
        Console.WriteLine();
        Console.WriteLine("   t (s)   f0 (Hz)   conf   RMS (dBFS)");

        var yin = new YinPitchDetector(DspMath.SampleRate, range.MinFrequency(), range.MaxFrequency());
        // Sonoridad cada 10 ms, para el resumen y para elegir las tramas del centroide.
        var voicedSlots = new bool[x.Length / StatsStep + 1];
        var track = new List<(double Seconds, double Hz)>();
        for (int i = 0; i < x.Length; i++)
        {
            yin.Push(x[i]);
            int done = i + 1;
            if (done % StatsStep == 0)
            {
                bool voiced = yin.IsVoiced;
                voicedSlots[done / StatsStep - 1] = voiced;
                if (voiced) track.Add((AudioFiles.Seconds(done), DspMath.SampleRate / yin.PeriodSamples));
            }
            if (done % ReportStep == 0)
            {
                double rms = DspMath.Rms(x.AsSpan(done - ReportStep, ReportStep));
                string f0 = yin.IsVoiced ? $"{DspMath.SampleRate / yin.PeriodSamples:0.0}" : "-";
                Console.WriteLine($"{AudioFiles.Seconds(done),8:0.00}  {f0,8}   {yin.Confidence,4:0.00}   {AudioFiles.Db(rms),8}");
            }
        }

        int slots = x.Length / StatsStep;
        var spectrum = new Spectrum();
        double centroidAll = spectrum.Centroid(x);
        double centroidVoiced = spectrum.Centroid(x, center => center / StatsStep < slots && voicedSlots[center / StatsStep]);

        Console.WriteLine();
        Console.WriteLine("Resumen");
        Console.WriteLine($"  Sonoro:     {(slots == 0 ? 0 : 100.0 * track.Count / slots):0.0} % de {slots} tramas de 10 ms");
        if (track.Count > 0)
        {
            var f0s = track.Select(t => t.Hz).Order().ToList();
            double median = Percentile(f0s, 0.5);
            Console.WriteLine($"  f0:         mediana {median:0.0} Hz · p10 {Percentile(f0s, 0.1):0.0} · p90 {Percentile(f0s, 0.9):0.0}" +
                $" · mín {f0s[0]:0.0} · máx {f0s[^1]:0.0} Hz");

            // Errores de octava, o ruido tomado por voz.
            var outliers = track.Where(t => t.Hz > 1.6 * median || t.Hz < 0.6 * median).ToList();
            string where = string.Join(", ", outliers.Take(8).Select(t => $"{t.Seconds:0.00} s ({t.Hz:0} Hz)"));
            Console.WriteLine($"  Atípicos:   {outliers.Count} tramas ({100.0 * outliers.Count / track.Count:0.0} %) fuera de 0,6-1,6 × la mediana" +
                (outliers.Count > 0 ? $": {where}{(outliers.Count > 8 ? "..." : "")}" : ""));
        }
        else
        {
            Console.WriteLine("  f0:         - (no se ha detectado voz)");
        }
        Console.WriteLine($"  Pico:       {AudioFiles.Db(DspMath.Peak(x))} dBFS");
        Console.WriteLine($"  RMS:        {AudioFiles.Db(DspMath.Rms(x))} dBFS");
        Console.WriteLine($"  Centroide:  {Hz(centroidVoiced)} en tramos sonoros · {Hz(centroidAll)} en todo (50 Hz-8 kHz)");
        ProcessCommand.WarnIfUnhealthy(x, "el archivo");
        return 0;
    }

    private static string Hz(double value) => double.IsNaN(value) ? "-" : $"{value:0} Hz";

    private static double Percentile(List<double> sorted, double p)
    {
        double index = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = Math.Min(sorted.Count - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}
