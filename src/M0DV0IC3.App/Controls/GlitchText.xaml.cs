using System.Windows;
using System.Windows.Controls;

namespace M0DV0IC3.App.Controls;

/// <summary>
/// Texto con el efecto glitch del logo: copias lima y violeta desplazadas detrás del texto. El desplazamiento es
/// proporcional al tamaño de la letra (<see cref="Strength"/> veces el FontSize).
/// </summary>
public partial class GlitchText : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(GlitchText), new PropertyMetadata(""));

    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(GlitchText), new PropertyMetadata(0.07, (d, _) => ((GlitchText)d).UpdateShift()));

    static GlitchText()
    {
        FontSizeProperty.OverrideMetadata(typeof(GlitchText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits, (d, _) => ((GlitchText)d).UpdateShift()));
    }

    public GlitchText()
    {
        InitializeComponent();
        UpdateShift();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Desplazamiento de las copias, en proporción al tamaño de la letra.</summary>
    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    private void UpdateShift()
    {
        if (LimeShift is null) return;
        double shift = Math.Max(1, FontSize * Strength);
        LimeShift.X = -shift;
        LimeShift.Y = -shift * 0.25;
        VioletShift.X = shift;
        VioletShift.Y = shift * 0.25;
    }
}
