using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows;
using H.NotifyIcon.Core;
using H.NotifyIcon;
using M0DV0IC3.App.Localization;

namespace M0DV0IC3.App.Services;

/// <summary>Icono de la bandeja del sistema con su menú (Mostrar / Voz ON-OFF / Salir).</summary>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;

    /// <param name="voiceSource">Objeto con la propiedad booleana <paramref name="voiceProperty"/> (la voz ON/OFF).</param>
    public TrayService(object voiceSource, string voiceProperty, Action show, Action exit)
    {
        var showItem = new MenuItem { Header = Loc.T("Mostrar M0DV0IC3"), FontWeight = FontWeights.SemiBold };
        showItem.Click += (_, _) => show();

        var voiceItem = new MenuItem { Header = Loc.T("Voz activada"), IsCheckable = true };
        voiceItem.SetBinding(MenuItem.IsCheckedProperty, new Binding(voiceProperty) { Source = voiceSource, Mode = BindingMode.TwoWay });

        var exitItem = new MenuItem { Header = Loc.T("Salir") };
        exitItem.Click += (_, _) => exit();

        var menu = new ContextMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(voiceItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
            ToolTipText = "M0DV0IC3",
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
        };
        _icon.TrayMouseDoubleClick += (_, _) => show();
        _icon.TrayLeftMouseUp += (_, _) => show();

        // Sin "modo eficiencia": bajaría la prioridad del proceso y el audio en tiempo real lo notaría.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public string ToolTipText
    {
        get => _icon.ToolTipText;
        set => _icon.ToolTipText = value;
    }

    public void ShowTip(string title, string message)
    {
        try
        {
            _icon.ShowNotification(title, message, NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo mostrar el aviso de la bandeja", ex);
        }
    }

    public void Dispose() => _icon.Dispose();
}
