using System.Security.Principal;
using System.Windows;

namespace M0DV0IC3.App.Services;

/// <summary>Cuadros de mensaje con el título de la app, centrados en la ventana si está visible.</summary>
public static class Dialogs
{
    private const string Title = "M0DV0IC3";

    public static bool Confirm(string message) =>
        Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warning(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Application.Current?.MainWindow;
        return owner is { IsVisible: true }
            ? MessageBox.Show(owner, message, Title, buttons, image)
            : MessageBox.Show(message, Title, buttons, image);
    }
}

public static class Elevation
{
    /// <summary>La app se está ejecutando como administrador.</summary>
    public static bool IsElevated { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
