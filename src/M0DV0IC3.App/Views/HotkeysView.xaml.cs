using System.Windows.Controls;

namespace M0DV0IC3.App.Views;

public partial class HotkeysView : UserControl
{
    public HotkeysView() => InitializeComponent();

    // Al buscar, los resultados se ven desde arriba aunque antes estuvieras abajo de la lista.
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => List.ScrollToTop();
}
