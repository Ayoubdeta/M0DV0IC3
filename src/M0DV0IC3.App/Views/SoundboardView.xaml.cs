using System.Windows;
using System.Windows.Controls;
using M0DV0IC3.App.ViewModels;
using M0DV0IC3.Audio.Soundboard;

namespace M0DV0IC3.App.Views;

/// <summary>Solo arrastrar y soltar: los archivos se pasan al view model.</summary>
public partial class SoundboardView : UserControl
{
    public SoundboardView() => InitializeComponent();

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        UpdateEffects(e);
        DropOverlay.Visibility = e.Effects == DragDropEffects.Copy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDragOver(object sender, DragEventArgs e) => UpdateEffects(e);

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (DataContext is not SoundboardViewModel viewModel || GetFiles(e) is not { Length: > 0 } files) return;
        e.Handled = true;
        await viewModel.AddFilesAsync(files);
    }

    private static void UpdateEffects(DragEventArgs e)
    {
        e.Effects = GetFiles(e)?.Any(SoundLoader.IsSupported) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static string[]? GetFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
}
