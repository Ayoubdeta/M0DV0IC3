using M0DV0IC3.Audio.Realtime;
using M0DV0IC3.Audio.Wasapi;
using NAudio.Wave;
using Xunit.Abstractions;

namespace M0DV0IC3.Tests;

public sealed class RealtimeTests(ITestOutputHelper output)
{
    [Fact]
    public void Ring_buffer_wraps_around()
    {
        var ring = new SpscRingBuffer(8);
        var dest = new float[8];
        for (int round = 0; round < 10; round++)
        {
            Assert.Equal(5, ring.Write([1, 2, 3, 4, 5]));
            Assert.Equal(5, ring.Read(dest));
            Assert.Equal([1f, 2, 3, 4, 5], dest[..5]);
        }
        Assert.Equal(8, ring.Write(new float[10]));
        Assert.Equal(8, ring.Available);
    }

    [Fact]
    public async Task Ring_buffer_keeps_order_across_threads()
    {
        var ring = new SpscRingBuffer(1024);
        const int total = 2_000_000;
        var producer = Task.Run(() =>
        {
            var chunk = new float[97];
            int next = 0;
            while (next < total)
            {
                int n = Math.Min(chunk.Length, total - next);
                for (int i = 0; i < n; i++) chunk[i] = next + i;
                int written = ring.Write(chunk.AsSpan(0, n));
                next += written;
                if (written == 0) Thread.SpinWait(10);
            }
        });

        var buffer = new float[61];
        int expected = 0;
        while (expected < total)
        {
            int read = ring.Read(buffer);
            for (int i = 0; i < read; i++)
            {
                // Los floats representan exactamente los enteros hasta 2^24.
                Assert.Equal(expected, (int)buffer[i]);
                expected++;
            }
            if (read == 0) Thread.SpinWait(10);
        }
        await producer;
    }

    [Theory]
    [InlineData(480, 480, 100)]
    [InlineData(480, 128, -150)]
    [InlineData(144, 480, 250)]
    public void Drift_reader_keeps_buffer_low_without_underruns(int producerBlock, int consumerBlock, double driftPpm)
    {
        // Simulación por eventos: productor y consumidor con relojes que difieren driftPpm.
        const int rate = 48000;
        int target = 96;
        var ring = new SpscRingBuffer(1 << 14);
        var reader = new DriftCompensatedReader(ring, target, rate);
        var produce = new float[producerBlock];
        var consume = new float[consumerBlock];

        double producerPeriod = producerBlock / (rate * (1 + driftPpm * 1e-6));
        double consumerPeriod = consumerBlock / (double)rate;
        double nextProduce = 0, nextConsume = 0.001;
        int maxFillLate = 0, underrunsAtSettle = -1;
        double phase = 0;
        long Ticks(double seconds) => (long)(seconds * System.Diagnostics.Stopwatch.Frequency);

        for (double t = 0; t < 60; )
        {
            if (nextProduce <= nextConsume)
            {
                t = nextProduce;
                for (int i = 0; i < produce.Length; i++) produce[i] = (float)Math.Sin(phase += 0.01);
                ring.Write(produce, Ticks(t));
                nextProduce += producerPeriod;
            }
            else
            {
                t = nextConsume;
                reader.Render(consume, Ticks(t));
                nextConsume += consumerPeriod;
                if (t > 20)
                {
                    if (underrunsAtSettle < 0) underrunsAtSettle = reader.Underruns;
                    maxFillLate = Math.Max(maxFillLate, ring.Available);
                }
            }
        }

        // Cada muestra espera el margen más el desfase entre los dos relojes, que como mucho es un bloque del productor.
        double maxWaitMs = (target + producerBlock + consumerBlock) * 1000.0 / rate;
        output.WriteLine($"cortes totales {reader.Underruns}, tras estabilizar {reader.Underruns - underrunsAtSettle}, nivel máx {maxFillLate}, medio {reader.AverageFill:F0}, espera media {reader.AverageWaitMs:F2} ms, ratio {reader.Ratio:F5}");
        Assert.Equal(underrunsAtSettle, reader.Underruns);
        Assert.True(maxFillLate < target + producerBlock + consumerBlock + 200, $"el buffer crece demasiado: {maxFillLate}");
        Assert.InRange(reader.AverageWaitMs, target * 1000.0 / rate * 0.5, maxWaitMs);
    }

    [Fact]
    public void Ring_buffer_measures_how_long_each_sample_waits()
    {
        var ring = new SpscRingBuffer(1024);
        long tick = System.Diagnostics.Stopwatch.Frequency / 1000;
        ring.Write(new float[100], arrivalTimestamp: 0);
        ring.Write(new float[100], arrivalTimestamp: 4 * tick);

        // Se leen 150 en t = 10 ms: 100 del primer bloque (esperaron 10 ms) y 50 del segundo (6 ms).
        long sum = ring.SumWaitTicks(ring.ReadPosition, 150, 10 * tick);
        Assert.Equal((100 * 10 + 50 * 6) / 150.0, sum / 150.0 / tick, 6);
        ring.Advance(150);

        // El resto del segundo bloque, en t = 12 ms: esperaron 8 ms.
        sum = ring.SumWaitTicks(ring.ReadPosition, 50, 12 * tick);
        Assert.Equal(8.0, sum / 50.0 / tick, 6);
    }

    [Fact]
    public void Drift_reader_output_is_a_clean_copy_at_unity_ratio()
    {
        var ring = new SpscRingBuffer(1 << 12);
        var reader = new DriftCompensatedReader(ring, 0);
        var input = TestSignals.Sine(440, 0.1);
        ring.Write(input);
        var result = new float[2000];
        reader.Render(result);

        int lag = TestSignals.BestLag(input, result, 8);
        for (int i = 100; i < 1500; i++) Assert.Equal(input[i], result[i + lag], 4);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(16, false)]
    [InlineData(24, false)]
    public void Sample_converter_roundtrips_stereo(int bits, bool isFloat)
    {
        var format = new WaveFormatExtensible(48000, bits, 2, isFloat, bits, 0x3);
        var layout = SampleLayout.From(format);
        var source = TestSignals.Sine(1000, 0.01, amplitude: 0.7f);
        var bytes = new byte[source.Length * layout.BlockAlign];
        var back = new float[source.Length];

        unsafe
        {
            fixed (byte* p = bytes)
            {
                SampleConverter.FromMono(source, (IntPtr)p, source.Length, layout);
                SampleConverter.ToMono((IntPtr)p, source.Length, layout, back);
            }
        }

        double tolerance = bits == 16 ? 1e-4 : 1e-6;
        for (int i = 0; i < source.Length; i++) Assert.True(Math.Abs(source[i] - back[i]) < tolerance);
    }
}
