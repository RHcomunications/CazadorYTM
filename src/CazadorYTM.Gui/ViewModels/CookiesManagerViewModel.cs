namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class CookiesManagerViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public event Action? RequestClose;

    public ObservableCollection<string> Browsers { get; } = new()
    {
        "Chrome", "Firefox", "Edge", "Brave", "Opera", "Vivaldi", "Chromium"
    };

    [ObservableProperty]
    private string _selectedBrowser = "Chrome";

    [ObservableProperty]
    private bool _saveAsJson = false;

    [ObservableProperty]
    private bool _isExtracting = false;

    [ObservableProperty]
    private string _autoExtractStatus = "Selecciona tu navegador e inicia la extracción.";

    [ObservableProperty]
    private string _manualCookiesText = string.Empty;

    [ObservableProperty]
    private string _manualStatus = "Listo para pegar cookies de YouTube.";

    [ObservableProperty]
    private string _currentCookiesPath = string.Empty;

    [ObservableProperty]
    private string _statusSummary = "Comprobando estado de cookies...";

    [ObservableProperty]
    private bool _hasActiveCookies = false;

    public CookiesManagerViewModel()
    {
        _config = new AppConfig();
        var savedBrowser = _config.Get("cookies_browser", "Chrome");
        if (Browsers.Contains(savedBrowser))
        {
            _selectedBrowser = savedBrowser;
        }

        RefreshStatus();
    }

    public void RefreshStatus()
    {
        CurrentCookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        var status = CookieService.GetCookiesStatus(CurrentCookiesPath);

        HasActiveCookies = status.Exists;

        if (status.Exists)
        {
            var authStr = status.HasYouTubeAuthCookies
                ? "Sesión de YouTube activa y verificada"
                : "Cookies presentes, pero sin tokens de sesión detectados";
            StatusSummary = $"Archivo: {Path.GetFileName(CurrentCookiesPath)} ({status.FormattedSize}, {status.EstimatedCookieCount} cookies)\nEstado: {authStr}";
        }
        else
        {
            StatusSummary = "No se ha configurado ningún archivo de cookies todavía. Puedes extraerlas automáticamente o pegarlas desde tu navegador.";
        }
    }

    [RelayCommand]
    public async Task ExtractFromBrowser()
    {
        IsExtracting = true;
        AutoExtractStatus = $"Extrayendo cookies de sesión desde {SelectedBrowser}... Por favor espera.";

        try
        {
            var targetPath = Helpers.GetDefaultCookiesPath();
            var result = await CookieService.ExtractCookiesFromBrowserAsync(
                SelectedBrowser,
                targetPath,
                SaveAsJson,
                msg => LogService.Instance.Info(msg));

            AutoExtractStatus = result.Message;

            if (result.Success)
            {
                _config.Set("cookies_browser", SelectedBrowser);
                _config.Set("cookies_path", targetPath);
                _config.Save();
                RefreshStatus();
                NotificationService.Instance.Notify("Cookies de YouTube", "Cookies extraídas y guardadas con éxito.", NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.Notify("Cookies de YouTube", "No se pudieron extraer automáticamente. Prueba con la Vía 2 (Asistente).", NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            AutoExtractStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsExtracting = false;
        }
    }

    [RelayCommand]
    public void OpenRobotsUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.youtube.com/robots.txt",
                UseShellExecute = true
            });
            ManualStatus = "Abriendo https://www.youtube.com/robots.txt en tu navegador predeterminado...";
        }
        catch (Exception ex)
        {
            ManualStatus = $"No se pudo abrir el navegador: {ex.Message}";
        }
    }

    [RelayCommand]
    public void PasteClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                ManualCookiesText = text;
                ManualStatus = $"Texto pegado desde el portapapeles ({text.Length} caracteres). Pulsa 'Validar y Guardar Cookies'.";
            }
            else
            {
                ManualStatus = "El portapapeles no contiene texto.";
            }
        }
        catch (Exception ex)
        {
            ManualStatus = $"Error al leer el portapapeles: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearManualText()
    {
        ManualCookiesText = string.Empty;
        ManualStatus = "Texto limpiado.";
    }

    [RelayCommand]
    public async Task SaveManualCookies()
    {
        if (string.IsNullOrWhiteSpace(ManualCookiesText))
        {
            ManualStatus = "Por favor pega el contenido de las cookies antes de guardar.";
            return;
        }

        ManualStatus = "Procesando y validando cookies...";
        var targetPath = Helpers.GetDefaultCookiesPath();

        var result = await CookieService.SaveCookiesFromTextAsync(
            ManualCookiesText,
            targetPath,
            SaveAsJson);

        ManualStatus = result.Message;

        if (result.Success)
        {
            _config.Set("cookies_path", targetPath);
            _config.Save();
            RefreshStatus();
            NotificationService.Instance.Notify("Cookies de YouTube", "Cookies guardadas y validadas exitosamente.", NotificationType.Success);
        }
        else
        {
            NotificationService.Instance.NotifyError("Cookies de YouTube", result.Message);
        }
    }

    [RelayCommand]
    public async Task ExportCookies()
    {
        if (!File.Exists(CurrentCookiesPath))
        {
            NotificationService.Instance.NotifyWarning("Exportar Cookies", "No hay cookies guardadas para exportar.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar archivo de cookies de Cazador YTM",
            Filter = "Archivo Netscape (*.txt)|*.txt|Archivo JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
            FileName = "cazador_cookies.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            var asJson = dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            var result = await CookieService.ExportCookiesAsync(CurrentCookiesPath, dialog.FileName, asJson);

            if (result.Success)
            {
                NotificationService.Instance.Notify("Exportar Cookies", "Archivo exportado con éxito.", NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError("Exportar Cookies", result.Message);
            }
        }
    }

    [RelayCommand]
    public async Task ImportCookies()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar archivo de cookies existente",
            Filter = "Archivos de Cookies (*.txt;*.json)|*.txt;*.json|Todos los archivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var targetPath = Helpers.GetDefaultCookiesPath();
            var result = await CookieService.ImportCookiesAsync(dialog.FileName, targetPath);

            if (result.Success)
            {
                _config.Set("cookies_path", targetPath);
                _config.Save();
                RefreshStatus();
                NotificationService.Instance.Notify("Importar Cookies", result.Message, NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError("Importar Cookies", result.Message);
            }
        }
    }

    [RelayCommand]
    public void OpenCookiesFolder()
    {
        try
        {
            var dir = Helpers.GetAppDataDir();
            if (Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    public void DeleteCookies()
    {
        if (!File.Exists(CurrentCookiesPath)) return;

        var res = MessageBox.Show(
            "¿Estás seguro de que deseas eliminar el archivo de cookies guardado?",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            try
            {
                File.Delete(CurrentCookiesPath);
                var jsonPath = Path.ChangeExtension(CurrentCookiesPath, ".json");
                if (File.Exists(jsonPath)) File.Delete(jsonPath);

                RefreshStatus();
                NotificationService.Instance.Notify("Cookies de YouTube", "Cookies eliminadas correctamente.", NotificationType.Info);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.NotifyError("Error", $"No se pudo eliminar el archivo: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}
