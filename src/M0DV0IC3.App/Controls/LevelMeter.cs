using System.Windows;
using System.Windows.Media;

namespace M0DV0IC3.App.Controls;

/// <summary>Medidor de nivel horizontal (0..1) dibujado a mano: lima del logo, amarillo cerca de 0 dB y rojo al saturar.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xB4, 0x78, 0xFF)));

    private static readonly Brush FillBrush = Freeze(new LinearGradientBrush(
        [
            new GradientStop(Color.FromRgb(0x7C, 0xD6, 0x2E), 0.0),
            new GradientStop(Color.FromRgb(0xA3, 0xF5, 0x3B), 0.70),
            new GradientStop(Color.FromRgb(0xFF, 0xD2, 0x4D), 0.86),
            new GradientStop(Color.FromRgb(0xFF, 0x4F, 0x7A), 1.0),
        ],
        new Point(0, 0), new Point(1, 0))
    {
        MappingMode = BrushMappingMode.RelativeToBoundingBox,
    });

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 8);

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double radius = bounds.Height / 2;
        dc.DrawRoundedRectangle(TrackBrush, null, bounds, radius, radius);

        double level = Math.Clamp(Level, 0, 1);
        if (level <= 0) return;
        // El degradado ocupa todo el ancho y se recorta: el color depende de la posición, no del nivel.
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width * level, bounds.Height)));
        dc.DrawRoundedRectangle(FillBrush, null, bounds, radius, radius);
        dc.Pop();
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
