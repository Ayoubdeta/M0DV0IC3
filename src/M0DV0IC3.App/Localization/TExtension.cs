using System.Windows.Markup;

namespace M0DV0IC3.App.Localization;

/// <summary>Un texto de la interfaz en XAML: <c>Text="{l:T 'Parar todo'}"</c> (en inglés, "Stop all").</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
