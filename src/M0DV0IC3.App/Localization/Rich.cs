using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace M0DV0IC3.App.Localization;

/// <summary>
/// Párrafos con negritas que se traducen enteros: <c>l:Rich.Text="{l:T 'Elige **CABLE Output** como micrófono.'}"</c>.
/// Lo que va entre ** sale en negrita. Así cada párrafo es un solo texto que traducir, en vez de trozos sueltos.
/// </summary>
public static class Rich
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Rich), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;
        block.Inlines.Clear();
        string text = e.NewValue as string ?? "";
        string[] parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            block.Inlines.Add(i % 2 == 1 ? new Bold(run) : run);
        }
    }
}
