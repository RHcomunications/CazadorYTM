namespace CazadorYTM.Gui;

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ModernWpf;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private const string MutexName = "Global\\CazadorYTM_SingleInstance_Mutex_2026";

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Activate existing instance window
            try
            {
                var current = Process.GetCurrentProcess();
                var other = Process.GetProcessesByName(current.ProcessName)
                    .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);

                if (other != null)
                {
                    ShowWindow(other.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(other.MainWindowHandle);
                }
            }
            catch { }

            Shutdown();
            return;
        }

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

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
        catch { }

        base.OnExit(e);
    }
}