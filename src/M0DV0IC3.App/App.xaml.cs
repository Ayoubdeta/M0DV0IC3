using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using M0DV0IC3.App.Services;
using M0DV0IC3.App.ViewModels;

namespace M0DV0IC3.App;

/// <summary>
/// Arranque y cierre: instancia única, manejo global de errores, servicios, ventana y bandeja.
/// Cerrar la ventana la oculta en la bandeja (si está activado); "Salir" cierra de verdad.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\M0DV0IC3.InstanciaUnica";
    private const string ShowEventName = @"Local\M0DV0IC3.Mostrar";
    private const string RestartArgument = "--reinicio";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private SettingsService? _settings;
    private AudioService? _audio;
    private HotkeyService? _hotkeys;
    private TrayService? _tray;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private int _showingError;

    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            AppPaths.EnsureCreated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin carpeta de datos la app funciona igual, pero no guarda nada.
        }
        Log.Rotate();
        RegisterExceptionHandlers();

        if (!AcquireSingleInstance(waitForPrevious: e.Args.Contains(RestartArgument)))
        {
            Shutdown();
            return;
        }

        Log.Info($"Inicio de M0DV0IC3 {typeof(App).Assembly.GetName().Version} (administrador: {(Elevation.IsElevated ? "sí" : "no")})");
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        // Los bindings formatean con la cultura del sistema (coma decimal), no con en-US.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        try
        {
            _settings = SettingsService.Load();
            _audio = new AudioService();
            _window = new MainWindow();
            MainWindow = _window;

            // El HWND existe desde ya (aunque se inicie minimizado) y sobrevive a Hide(): ahí van los atajos.
            IntPtr hwnd = new WindowInteropHelper(_window).EnsureHandle();
            _hotkeys = new HotkeyService(hwnd);
            _viewModel = new MainViewModel(_settings, _audio, _hotkeys, RestartAsAdministrator);
            _window.DataContext = _viewModel;

            _tray = new TrayService(_viewModel, nameof(MainViewModel.VoiceEnabled), ShowMainWindow, ExitApplication);
            _tray.ToolTipText = _viewModel.TrayToolTip;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.TrayToolTip) && _tray is not null) _tray.ToolTipText = _viewModel.TrayToolTip;
            };

            _viewModel.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo iniciar la app", ex);
            MessageBox.Show($"M0DV0IC3 no ha podido arrancar:\n\n{ex.Message}\n\nMás detalles en {AppPaths.LogFile}",
                "M0DV0IC3", MessageBoxButton.OK, MessageBoxImage.Error);
            ExitApplication();
            return;
        }

        if (!_settings.Current.StartMinimized)
        {
            _window.Show();
        }
        else if (!_settings.Current.MinimizeToTray)
        {
            _window.WindowState = WindowState.Minimized;
            _window.Show();
        }
        // Si no, arranca solo en la bandeja.
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        ExitApplication();
    }

    public void ShowMainWindow()
    {
        if (_window is null || IsExiting) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // Truco para traerla delante aunque otra app tenga el foco.
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    /// <summary>La X de la ventana: a la bandeja o salir, según los ajustes.</summary>
    public void OnMainWindowCloseRequested()
    {
        if (_settings?.Current.MinimizeToTray != true)
        {
            // Se llama desde Closing: la ventana no se puede cerrar otra vez hasta que termine ese evento.
            Dispatcher.BeginInvoke(new Action(ExitApplication));
            return;
        }

        _window?.Hide();
        if (!_settings.Current.TrayTipShown)
        {
            _tray?.ShowTip("M0DV0IC3 sigue funcionando",
                "Tu voz y los atajos siguen activos. Doble clic en este icono para abrir la ventana; clic derecho → Salir para cerrarla.");
            _settings.Current.TrayTipShown = true;
            _settings.ScheduleSave();
        }
    }

    /// <summary>Salida de verdad: para el audio, guarda los ajustes y libera todo.</summary>
    public void ExitApplication()
    {
        if (IsExiting) return;
        IsExiting = true;

        TryRun("cerrar la vista", () => _viewModel?.Shutdown());
        TryRun("quitar los atajos", () => _hotkeys?.Dispose());
        TryRun("parar el audio", () => _audio?.Dispose());
        TryRun("guardar los ajustes", () => _settings?.SaveNow());
        TryRun("quitar el icono de la bandeja", () => _tray?.Dispose());
        TryRun("cerrar la ventana", () => _window?.Close());

        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // No era nuestro (no debería pasar).
            }
        }
        _mutex?.Dispose();
        Log.Info("Salida de M0DV0IC3");
        Shutdown();
    }

    private void RestartAsAdministrator()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, RestartArgument) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // El usuario ha cancelado el aviso de Windows.
        }
        catch (Win32Exception ex)
        {
            Log.Warn("No se pudo reiniciar como administrador", ex);
            Dialogs.Error($"No se pudo reiniciar como administrador:\n{ex.Message}");
            return;
        }
        ExitApplication();
    }

    private bool AcquireSingleInstance(bool waitForPrevious)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            _ownsMutex = createdNew;
            if (!createdNew)
            {
                // Tras "Reiniciar como administrador", la instancia anterior tarda un momento en cerrarse.
                try
                {
                    _ownsMutex = _mutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    _ownsMutex = true;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // La otra instancia se ejecuta como administrador.
            _ownsMutex = false;
        }

        if (_ownsMutex)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)), null, Timeout.Infinite, executeOnlyOnce: false);
            return true;
        }

        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var other))
            {
                using (other) other.Set();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // No se puede avisar a la otra instancia (p. ej. es de administrador); basta con el mensaje.
        }
        MessageBox.Show("M0DV0IC3 ya está abierto.\n\nBúscalo en la bandeja del sistema, junto al reloj.",
            "M0DV0IC3", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("EXCEPCIÓN NO CONTROLADA (hilo de la interfaz)", e.Exception);
            ShowErrorMessage(e.Exception, fatal: false);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var error = e.ExceptionObject as Exception;
            Log.Error("EXCEPCIÓN NO CONTROLADA", error);
            if (e.IsTerminating) ShowErrorMessage(error, fatal: true);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Excepción no observada en una tarea", e.Exception);
            e.SetObserved();
        };
    }

    private void ShowErrorMessage(Exception? error, bool fatal)
    {
        // Si salta otro error mientras se muestra el mensaje, no se encadenan ventanas.
        if (Interlocked.Exchange(ref _showingError, 1) != 0) return;
        try
        {
            string intro = fatal
                ? "Ha ocurrido un error grave y M0DV0IC3 tiene que cerrarse."
                : "Ha ocurrido un error inesperado. M0DV0IC3 intentará seguir funcionando.";
            MessageBox.Show($"{intro}\n\n{error?.Message}\n\nLos detalles están en:\n{AppPaths.LogFile}",
                "M0DV0IC3", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Sin interfaz para avisar: queda el log.
        }
        finally
        {
            Volatile.Write(ref _showingError, 0);
        }
    }

    private static void TryRun(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Error al {what}", ex);
        }
    }
}
