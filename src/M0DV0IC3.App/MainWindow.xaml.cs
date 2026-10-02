using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using M0DV0IC3.App.Services;
using M0DV0IC3.App.ViewModels;

namespace M0DV0IC3.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        IsVisibleChanged += (_, _) => ViewModel?.SetWindowVisible(IsVisible);
        Deactivated += (_, _) => ViewModel?.Hotkeys.CancelCapture();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>Durante "Cambiar atajo", la siguiente combinación de teclas va al view model.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (ViewModel?.Hotkeys is { IsCapturing: true } hotkeys)
        {
            Key key = e.Key switch
            {
                Key.System => e.SystemKey,
                Key.ImeProcessed => e.ImeProcessedKey,
                Key.DeadCharProcessed => e.DeadCharProcessedKey,
                _ => e.Key,
            };
            e.Handled = hotkeys.HandleCaptureKey(key, Keyboard.Modifiers);
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Con WindowChrome, una ventana maximizada se sale de la pantalla lo que mide su borde de redimensionar:
    /// se compensa con un margen para que no se corte el contenido.
    /// </summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    // Igual que la X de Windows: OnClosing decide si se minimiza a la bandeja.
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (App.IsExiting) return;
        e.Cancel = true;
        ((App)Application.Current).OnMainWindowCloseRequested();
    }
}
