namespace CazadorYTM.Gui.Services;

using System;
using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public class NotificationService
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());
    public static NotificationService Instance => _instance.Value;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_NONE = 0x00000000;
    private const int NIIF_INFO = 0x00000001;
    private const int NIIF_WARNING = 0x00000002;
    private const int NIIF_ERROR = 0x00000003;

    private const int NOTIFYICON_VERSION_4 = 4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private const int IDI_APPLICATION = 32512;
    private bool _iconAdded = false;
    private readonly object _lock = new();

    public void Notify(string title, string message, NotificationType type = NotificationType.Info)
    {
        // 1. Immediate audible feedback
        try
        {
            if (type == NotificationType.Success || type == NotificationType.Info)
                SystemSounds.Asterisk.Play();
            else if (type == NotificationType.Warning || type == NotificationType.Error)
                SystemSounds.Exclamation.Play();
        }
        catch { }

        // 2. Immediate Native Win32 Notification (0ms latency, shows directly as Cazador YTM)
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var mainWin = Application.Current.MainWindow;
                var hwnd = mainWin != null ? new WindowInteropHelper(mainWin).Handle : IntPtr.Zero;
                if (hwnd == IntPtr.Zero)
                    hwnd = Process.GetCurrentProcess().MainWindowHandle;

                var nid = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                    hWnd = hwnd,
                    uID = 1001,
                    uFlags = NIF_INFO | NIF_TIP | NIF_ICON | NIF_MESSAGE,
                    szTip = "Cazador YTM",
                    szInfoTitle = string.IsNullOrWhiteSpace(title) ? "Cazador YTM" : title.Trim(),
                    szInfo = message ?? string.Empty,
                    dwInfoFlags = type switch
                    {
                        NotificationType.Error => NIIF_ERROR,
                        NotificationType.Warning => NIIF_WARNING,
                        _ => NIIF_INFO
                    },
                    hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION),
                    uTimeoutOrVersion = NOTIFYICON_VERSION_4
                };

                lock (_lock)
                {
                    Shell_NotifyIcon(NIM_DELETE, ref nid);
                    Shell_NotifyIcon(NIM_ADD, ref nid);
                    Shell_NotifyIcon(NIM_SETVERSION, ref nid);
                    _iconAdded = true;
                }
            }
            catch { }
        }, DispatcherPriority.Normal);
    }

    public void Cleanup()
    {
        lock (_lock)
        {
            if (_iconAdded)
            {
                try
                {
                    var mainWin = Application.Current?.MainWindow;
                    var hwnd = mainWin != null ? new WindowInteropHelper(mainWin).Handle : IntPtr.Zero;
                    if (hwnd == IntPtr.Zero)
                        hwnd = Process.GetCurrentProcess().MainWindowHandle;

                    var nid = new NOTIFYICONDATA
                    {
                        cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                        hWnd = hwnd,
                        uID = 1001
                    };
                    Shell_NotifyIcon(NIM_DELETE, ref nid);
                    _iconAdded = false;
                }
                catch { }
            }
        }
    }

    public void NotifyDownloadComplete(string title, string filePath)
    {
        Notify("Descarga Completada", $"'{title}' se ha descargado correctamente.", NotificationType.Success);
    }

    public void NotifyBatchComplete(int successful, int total)
    {
        Notify("Descargas Finalizadas", $"Se han completado {successful} de {total} canciones con éxito.", NotificationType.Success);
    }

    public void NotifyError(string errorTitle, string errorMessage)
    {
        Notify(errorTitle, errorMessage, NotificationType.Error);
    }

    public void NotifyVerificationComplete(int total, int corrupt)
    {
        if (corrupt == 0)
        {
            Notify("Verificación de Integridad", $"Se verificaron {total} archivos de audio sin ningún error.", NotificationType.Success);
        }
        else
        {
            Notify("Verificación de Integridad", $"Se encontraron {corrupt} archivos con errores de {total} analizados.", NotificationType.Warning);
        }
    }
}
