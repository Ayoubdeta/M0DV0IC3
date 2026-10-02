using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace M0DV0IC3.App.Controls;

/// <summary>
/// Fila del editor de voces: etiqueta, slider y valor formateado. Con <see cref="OffText"/>, el valor
/// mínimo se muestra como "apagado". Doble clic en la fila: vuelve a <see cref="DefaultValue"/>.
/// </summary>
public partial class ParamSlider : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ParamSlider), new PropertyMetadata(""));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(ParamSlider), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ParamSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangeOrValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ParamSlider), new PropertyMetadata(0.0, OnRangeOrValueChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ParamSlider), new PropertyMetadata(1.0, OnRangeOrValueChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(ParamSlider), new PropertyMetadata(0.01));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(ParamSlider), new PropertyMetadata("{0:0.##}", OnFormatChanged));

    public static readonly DependencyProperty OffTextProperty = DependencyProperty.Register(
        nameof(OffText), typeof(string), typeof(ParamSlider), new PropertyMetadata(null, OnFormatChanged));

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(ParamSlider), new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(ParamSlider), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

    private bool _syncing;

    public ParamSlider()
    {
        InitializeComponent();
        SyncTrack();
        UpdateText();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Formato compuesto para el valor, por ejemplo "{0:0} Hz".</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    /// <summary>Texto para el valor mínimo cuando significa "efecto apagado".</summary>
    public string? OffText
    {
        get => (string?)GetValue(OffTextProperty);
        set => SetValue(OffTextProperty, value);
    }

    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public string ValueText => (string)GetValue(ValueTextProperty);

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (IsEnabled) Value = DefaultValue;
    }

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ParamSlider)d).UpdateText();

    private static void OnRangeOrValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ParamSlider)d;
        control.SyncTrack();
        control.UpdateText();
    }

    /// <summary>
    /// El slider interno se sincroniza a mano (rango antes que valor): con bindings, la coerción de
    /// Minimum/Maximum podría devolver al view model un valor recortado mientras se cargan.
    /// </summary>
    private void SyncTrack()
    {
        if (Track is null) return;
        _syncing = true;
        try
        {
            Track.Minimum = Minimum;
            Track.Maximum = Maximum;
            Track.Value = Value;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnTrackValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing) Value = e.NewValue;
    }

    private void UpdateText()
    {
        string text;
        if (OffText is not null && Value <= Minimum + 1e-9)
        {
            text = OffText;
        }
        else
        {
            try
            {
                text = string.Format(CultureInfo.CurrentCulture, Format, Value);
            }
            catch (FormatException)
            {
                text = Value.ToString("0.##", CultureInfo.CurrentCulture);
            }
        }
        SetValue(ValueTextPropertyKey, text);
    }
}
