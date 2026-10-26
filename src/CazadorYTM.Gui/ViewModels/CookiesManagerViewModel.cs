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
    private string _autoExtractStatus = string.Empty;

    [ObservableProperty]
    private string _manualCookiesText = string.Empty;

    [ObservableProperty]
    private string _manualStatus = string.Empty;

    [ObservableProperty]
    private string _currentCookiesPath = string.Empty;

    [ObservableProperty]
    private string _statusSummary = string.Empty;

    [ObservableProperty]
    private bool _hasActiveCookies = false;

    public CookiesManagerViewModel()
    {
        _config = new AppConfig();
        var loc = LocalizationService.Instance;
        _autoExtractStatus = loc["CookiesAutoExtractPrompt"];
        _manualStatus = loc["CookiesManualPrompt"];
        _statusSummary = loc["CookiesCheckingStatus"];

        var savedBrowser = _config.Get("cookies_browser", "Chrome");
        if (Browsers.Contains(savedBrowser))
        {
            _selectedBrowser = savedBrowser;
        }

        RefreshStatus();
    }

    public void RefreshStatus()
    {
        var loc = LocalizationService.Instance;
        CurrentCookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        var status = CookieService.GetCookiesStatus(CurrentCookiesPath);

        HasActiveCookies = status.Exists;

        if (status.Exists)
        {
            var authStr = status.HasYouTubeAuthCookies
                ? loc["CookiesActiveSession"]
                : loc["CookiesNoSessionTokens"];
            StatusSummary = $"Archivo: {Path.GetFileName(CurrentCookiesPath)} ({status.FormattedSize}, {status.EstimatedCookieCount} cookies)\nEstado: {authStr}";
        }
        else
        {
            StatusSummary = loc["CookiesNoFileConfigured"];
        }
    }

    [RelayCommand]
    public async Task ExtractFromBrowser()
    {
        var loc = LocalizationService.Instance;
        IsExtracting = true;
        AutoExtractStatus = string.Format(loc["CookiesExtracting"], SelectedBrowser);

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
                NotificationService.Instance.Notify(loc["CookiesWindowTitle"], loc["CookiesExtractSuccess"], NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.Notify(loc["CookiesWindowTitle"], loc["CookiesExtractFailed"], NotificationType.Warning);
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
        var loc = LocalizationService.Instance;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.youtube.com/robots.txt",
                UseShellExecute = true
            });
            ManualStatus = loc["CookiesOpeningRobots"];
        }
        catch (Exception ex)
        {
            ManualStatus = $"No se pudo abrir el navegador: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenCookiesExtensionChrome()
    {
        OpenExternalUrl(Constants.CookiesExtensionChromeUrl);
    }

    [RelayCommand]
    public void OpenCookiesExtensionFirefox()
    {
        OpenExternalUrl(Constants.CookiesExtensionFirefoxUrl);
    }

    private static void OpenExternalUrl(string url)
    {
        var loc = LocalizationService.Instance;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            NotificationService.Instance.NotifyError(loc["CookiesWindowTitle"], $"No se pudo abrir el navegador: {ex.Message}");
        }
    }

    [RelayCommand]
    public void PasteClipboard()
    {
        var loc = LocalizationService.Instance;
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                ManualCookiesText = text;
                ManualStatus = string.Format(loc["CookiesClipboardPasted"], text.Length);
            }
            else
            {
                ManualStatus = loc["CookiesClipboardEmpty"];
            }
        }
        catch (Exception ex)
        {
            ManualStatus = string.Format(loc["CookiesClipboardError"], ex.Message);
        }
    }

    [RelayCommand]
    public void ClearManualText()
    {
        var loc = LocalizationService.Instance;
        ManualCookiesText = string.Empty;
        ManualStatus = loc["CookiesTextCleared"];
    }

    [RelayCommand]
    public async Task SaveManualCookies()
    {
        var loc = LocalizationService.Instance;
        if (string.IsNullOrWhiteSpace(ManualCookiesText))
        {
            ManualStatus = loc["CookiesPasteRequired"];
            return;
        }

        ManualStatus = loc["CookiesValidating"];
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
            NotificationService.Instance.Notify(loc["CookiesWindowTitle"], loc["CookiesSavedSuccess"], NotificationType.Success);
        }
        else
        {
            NotificationService.Instance.NotifyError(loc["CookiesWindowTitle"], result.Message);
        }
    }

    [RelayCommand]
    public async Task ExportCookies()
    {
        var loc = LocalizationService.Instance;
        if (!File.Exists(CurrentCookiesPath))
        {
            NotificationService.Instance.NotifyWarning(loc["CookiesExportGroup"], loc["CookiesNoneToExport"]);
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
                NotificationService.Instance.Notify(loc["CookiesExportGroup"], loc["CookiesExportSuccess"], NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError(loc["CookiesExportGroup"], result.Message);
            }
        }
    }

    [RelayCommand]
    public async Task ImportCookies()
    {
        var loc = LocalizationService.Instance;
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
                NotificationService.Instance.Notify(loc["CookiesImportGroup"], result.Message, NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError(loc["CookiesImportGroup"], result.Message);
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
        var loc = LocalizationService.Instance;

        var confirmed = DialogService.Instance.ShowConfirmation(
            loc["CookiesDeleteConfirmMsg"],
            loc["CookiesDeleteConfirmTitle"]);

        if (confirmed)
        {
            try
            {
                File.Delete(CurrentCookiesPath);
                var jsonPath = Path.ChangeExtension(CurrentCookiesPath, ".json");
                if (File.Exists(jsonPath)) File.Delete(jsonPath);

                RefreshStatus();
                NotificationService.Instance.Notify(loc["CookiesWindowTitle"], loc["CookiesDeleteSuccess"], NotificationType.Info);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.NotifyError("Error", $"No se pudo eliminar el archivo: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void ExportLogs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar registro de errores de Cazador YTM",
            Filter = "Archivo de Registro (*.log)|*.log|Archivo de Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            FileName = $"cazador_registro_cookies_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };

        if (dialog.ShowDialog() == true)
        {
            if (LogService.Instance.ExportLogs(dialog.FileName))
            {
                NotificationService.Instance.Notify("Registro Exportado", $"Registro guardado en:\n{dialog.FileName}", NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError("Exportar Registro", "No se pudo guardar el archivo de registro.");
            }
        }
    }

    [RelayCommand]
    public void ContactSupport()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Constants.SupportTelegramUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            NotificationService.Instance.NotifyError("Soporte Técnico", $"No se pudo abrir el enlace de soporte: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}
