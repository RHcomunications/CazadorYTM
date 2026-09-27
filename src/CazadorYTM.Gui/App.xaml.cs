namespace CazadorYTM.Gui;

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ModernWpf;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception handlers for rock-solid stability
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogService.Instance.Error($"[CRÍTICO] Excepción no controlada en AppDomain: {ex}");
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogService.Instance.Error($"[ERROR] Excepción no controlada en Dispatcher UI: {args.Exception}");
            args.Handled = true; // Prevent abrupt app crash
            DialogService.Instance.ShowMessage($"Ocurrió un error en la interfaz:\n\n{args.Exception.Message}", "Error de Interfaz", MessageBoxImage.Error);
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogService.Instance.Error($"[ERROR] Excepción no observada en Tarea Asíncrona: {args.Exception}");
            args.SetObserved();
        };

        var config = new AppConfig();
        var lang = config.Get("language", "es");
        LocalizationService.Instance.SetLanguage(lang);

        var theme = config.Get("theme", "Automático");
        if (theme.Equals("Oscuro", StringComparison.OrdinalIgnoreCase))
        {
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        }
        else if (theme.Equals("Claro", StringComparison.OrdinalIgnoreCase))
        {
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
        }
        else
        {
            ThemeManager.Current.ApplicationTheme = null; // Auto / System
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            NotificationService.Instance.Cleanup();
            AudioPlayerService.Instance.Stop();
        }
        catch { }

        base.OnExit(e);
    }
}