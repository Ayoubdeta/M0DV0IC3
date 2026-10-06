using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using M0DV0IC3.App.ViewModels;

namespace M0DV0IC3.App.Views;

public partial class KaraokeView : UserControl
{
    private KaraokeViewModel? _viewModel;

    public KaraokeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = DataContext as KaraokeViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
        };
    }

    /// <summary>La línea que toca se queda en el centro de la letra.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KaraokeViewModel.CurrentIndex) || _viewModel is null) return;
        if (_viewModel.CurrentIndex < 0)
        {
            LyricsScroll.ScrollToTop();
            return;
        }
        // Después del cambio de tamaño de la línea actual, para que la cuenta salga bien.
        Dispatcher.BeginInvoke(() =>
        {
            if (LyricsList.ItemContainerGenerator.ContainerFromIndex(_viewModel.CurrentIndex) is not FrameworkElement line) return;
            var top = line.TransformToAncestor(LyricsScroll).Transform(new Point(0, 0)).Y + LyricsScroll.VerticalOffset;
            LyricsScroll.ScrollToVerticalOffset(Math.Max(0, top + line.ActualHeight / 2 - LyricsScroll.ViewportHeight / 2));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
