using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace M0DV0IC3.App.Controls;

/// <summary>
/// Forma de onda de un sonido con dos marcas que se arrastran: dónde empieza y dónde acaba. Lo que queda fuera
/// se ve oscurecido. Al pulsar, se mueve la marca más cercana al ratón.
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(double), typeof(WaveformView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(double), typeof(WaveformView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private const double MinimumSeconds = 0.05;

    private bool _draggingStart;
    private bool _dragging;

    public WaveformView()
    {
        Cursor = Cursors.SizeWE;
        Focusable = false;
        ToolTip = "Arrastra las marcas para elegir dónde empieza y dónde acaba el sonido";
    }

    public float[]? Peaks
    {
        get => (float[]?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    /// <summary>Duración del sonido entero, en segundos.</summary>
    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double Start
    {
        get => (double)GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public double End
    {
        get => (double)GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        dc.DrawRoundedRectangle(Brush("InkBrush", Color.FromRgb(0x12, 0x0B, 0x1C)), null, new Rect(0, 0, width, height), 8, 8);
        if (Peaks is not { Length: > 0 } peaks || Duration <= 0) return;

        var wave = Brush("LimeBrush", Color.FromRgb(0xA3, 0xF5, 0x3B));
        double mid = height / 2, bar = width / peaks.Length;
        float max = Math.Max(peaks.Max(), 1e-6f);
        for (int i = 0; i < peaks.Length; i++)
        {
            double h = Math.Max(1, peaks[i] / max * (height / 2 - 4));
            dc.DrawRectangle(wave, null, new Rect(i * bar, mid - h, Math.Max(1, bar - 0.5), 2 * h));
        }

        // Lo que se recorta, oscurecido, y las dos marcas.
        double x0 = X(Start), x1 = X(End);
        var shade = new SolidColorBrush(Color.FromArgb(0xC0, 0x12, 0x0B, 0x1C));
        if (x0 > 0) dc.DrawRectangle(shade, null, new Rect(0, 0, x0, height));
        if (x1 < width) dc.DrawRectangle(shade, null, new Rect(x1, 0, width - x1, height));
        var marker = Brush("AccentBrush", Color.FromRgb(0xB4, 0x78, 0xFF));
        foreach (double x in new[] { x0, x1 })
        {
            dc.DrawRectangle(marker, null, new Rect(x - 1.5, 0, 3, height));
            dc.DrawRoundedRectangle(marker, null, new Rect(x - 5, height / 2 - 9, 10, 18), 3, 3);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Duration <= 0) return;
        double x = e.GetPosition(this).X;
        _draggingStart = Math.Abs(x - X(Start)) <= Math.Abs(x - X(End));
        _dragging = CaptureMouse();
        MoveTo(x);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) MoveTo(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    private void MoveTo(double x)
    {
        double seconds = Math.Round(Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * Duration, 2);
        if (_draggingStart) Start = Math.Clamp(seconds, 0, Math.Max(0, End - MinimumSeconds));
        else End = Math.Clamp(seconds, Math.Min(Duration, Start + MinimumSeconds), Duration);
    }

    private double X(double seconds) => Math.Clamp(seconds / Duration, 0, 1) * ActualWidth;

    private Brush Brush(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
