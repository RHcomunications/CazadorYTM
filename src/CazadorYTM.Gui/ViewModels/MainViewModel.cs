namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public DownloadViewModel DownloadVM { get; }
    public PlayerViewModel PlayerVM { get; }
    public HistoryViewModel HistoryVM { get; }
    public RadarViewModel RadarVM { get; }
    public LogsViewModel LogsVM { get; }

    public ObservableCollection<MainTabItem> OpenTabs { get; } = new();

    [ObservableProperty]
    private MainTabItem? _selectedTab;

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    [ObservableProperty]
    private string _statusText = "Listo";

    [ObservableProperty]
    private System.Windows.Shell.TaskbarItemProgressState _taskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.None;

    [ObservableProperty]
    private double _taskbarProgressValue = 0.0;

    public string AppTitle => $"{Constants.AppTitle}";

    public MainViewModel()
    {
        _config = new AppConfig();

        DownloadVM = new DownloadViewModel();
        PlayerVM = new PlayerViewModel();
        HistoryVM = new HistoryViewModel();
        RadarVM = new RadarViewModel();
        LogsVM = new LogsViewModel();

        DownloadVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DownloadVM.IsBusy) ||
                e.PropertyName == nameof(DownloadVM.ProgressPercentage) ||
                e.PropertyName == nameof(DownloadVM.StatusText))
            {
                UpdateTaskbarProgress();
            }
        };

        HistoryVM.RetryRequested += (entry) =>
        {
            OpenOrFocusTab(MainTabType.Downloads);
            var target = !string.IsNullOrWhiteSpace(entry.Url) ? entry.Url : entry.Title ?? "";
            DownloadVM.InputText = target;
            NotificationService.Instance.Notify("Reintentar Descarga", $"Canción cargada en el panel de descargas: {target}", NotificationType.Info);
        };

        RadarVM.SendToDownloadRequested += (entry) =>
        {
            OpenOrFocusTab(MainTabType.Downloads);
            var target = !string.IsNullOrWhiteSpace(entry.Url) ? entry.Url : entry.Title ?? "";
            DownloadVM.InputText = target;
        };

        RadarVM.PlayTrackRequested += (streamUrl, title, artist) =>
        {
            OpenOrFocusTab(MainTabType.Player);
            PlayerVM.PlayRemoteTrack(streamUrl, title, artist);
        };

        // Inicializar con la pestaña inicial configurada por el usuario
        InitializeStartupTab();

        LogService.Instance.Info($"{Constants.AppTitle} iniciado correctamente.");
        _ = CheckForUpdatesSilentlyAsync();
    }

    private void InitializeStartupTab()
    {
        var startup = _config.Get("startup_tab", "Descarga");
        if (startup.Equals("Reproductor", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.Player);
        }
        else if (startup.Equals("Radar YTM", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.Radar);
        }
        else
        {
            OpenOrFocusTab(MainTabType.Downloads);
        }
    }

    public void OpenOrFocusTab(MainTabType tabType)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.TabType == tabType);
        if (existing != null)
        {
            SelectedTab = existing;
            SelectedTabIndex = OpenTabs.IndexOf(existing);
            return;
        }

        var loc = LocalizationService.Instance;
        MainTabItem newTab = tabType switch
        {
            MainTabType.Downloads => new MainTabItem(MainTabType.Downloads, loc.Get("TabDownloads", "Descarga"), DownloadVM),
            MainTabType.History => new MainTabItem(MainTabType.History, loc.Get("TabLibrary", "Biblioteca"), HistoryVM),
            MainTabType.Player => new MainTabItem(MainTabType.Player, loc.Get("TabPlayer", "Reproductor"), PlayerVM),
            MainTabType.Radar => new MainTabItem(MainTabType.Radar, loc.Get("TabRadar", "Radar YTM"), RadarVM),
            _ => new MainTabItem(MainTabType.Downloads, loc.Get("TabDownloads", "Descarga"), DownloadVM)
        };

        OpenTabs.Add(newTab);
        SelectedTab = newTab;
        SelectedTabIndex = OpenTabs.IndexOf(newTab);

        if (tabType == MainTabType.History)
        {
            HistoryVM.RefreshHistory();
        }
        else if (tabType == MainTabType.Radar && RadarVM.AllItems.Count == 0)
        {
            _ = RadarVM.RefreshCurrentSection();
        }
    }

    [RelayCommand]
    public void CloseCurrentTab()
    {
        if (SelectedTab == null || OpenTabs.Count <= 1)
        {
            // Si solo queda una pestaña, no permitir cerrar para evitar vista en blanco
            return;
        }

        var tabToClose = SelectedTab;
        var curIdx = OpenTabs.IndexOf(tabToClose);

        OpenTabs.Remove(tabToClose);

        var newIdx = Math.Clamp(curIdx, 0, OpenTabs.Count - 1);
        SelectedTab = OpenTabs[newIdx];
        SelectedTabIndex = newIdx;
    }

    [RelayCommand]
    public void CloseTab(MainTabItem? tab)
    {
        if (tab == null || OpenTabs.Count <= 1) return;

        var isSelected = tab == SelectedTab;
        var curIdx = OpenTabs.IndexOf(tab);

        OpenTabs.Remove(tab);

        if (isSelected)
        {
            var newIdx = Math.Clamp(curIdx, 0, OpenTabs.Count - 1);
            SelectedTab = OpenTabs[newIdx];
            SelectedTabIndex = newIdx;
        }
    }

    [RelayCommand]
    private void NavigateToTab(string tabIdentifier)
    {
        if (tabIdentifier == "0" || tabIdentifier.Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.Downloads);
        }
        else if (tabIdentifier == "1" || tabIdentifier.Equals("library", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.History);
        }
        else if (tabIdentifier == "2" || tabIdentifier.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.Player);
        }
        else if (tabIdentifier == "3" || tabIdentifier.Equals("radar", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.Radar);
        }
    }

    public void UpdateTaskbarProgress()
    {
        if (!DownloadVM.IsBusy)
        {
            TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            TaskbarProgressValue = 0.0;
            return;
        }

        var status = DownloadVM.StatusText ?? "";
        if (status.Contains("Error", StringComparison.OrdinalIgnoreCase) || status.Contains("Fallo", StringComparison.OrdinalIgnoreCase))
        {
            TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
            return;
        }

        if (DownloadVM.ProgressPercentage <= 0.0 || (DownloadVM.ProgressPercentage >= 100.0 && status.Contains("Procesando", StringComparison.OrdinalIgnoreCase)))
        {
            TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
            TaskbarProgressValue = 0.0;
        }
        else
        {
            TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
            TaskbarProgressValue = Math.Clamp(DownloadVM.ProgressPercentage / 100.0, 0.0, 1.0);
        }
    }

    private async Task CheckForUpdatesSilentlyAsync()
    {
        try
        {
            await Task.Delay(3000); // Wait for UI to stabilize
            var info = await AppUpdaterService.CheckAppUpdateAsync();
            if (info.HasUpdate)
            {
                NotificationService.Instance.Notify(
                    "Nueva versión disponible",
                    $"Cazador YTM v{info.LatestVersion} está disponible para descargar.",
                    NotificationType.Info);
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task CheckAppUpdate()
    {
        try
        {
            var info = await AppUpdaterService.CheckAppUpdateAsync();
            if (info.HasUpdate)
            {
                var notesPreview = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "" : $"\n\nNotas de la versión:\n{info.ReleaseNotes}";
                var confirmMsg = $"Se ha publicado una nueva versión de Cazador YTM:\n\n• Versión actual: v{info.CurrentVersion}\n• Versión disponible: v{info.LatestVersion}{notesPreview}\n\n¿Desea descargar e instalar la actualización automáticamente ahora?";

                var accept = DialogService.Instance.ShowConfirmation(confirmMsg, "Actualización de Cazador YTM");
                if (accept)
                {
                    if (string.IsNullOrEmpty(info.AssetDownloadUrl))
                    {
                        if (!string.IsNullOrEmpty(info.HtmlUrl))
                        {
                            Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
                        }
                        return;
                    }

                    var baseDir = Helpers.GetBaseDir();
                    NotificationService.Instance.Notify("Descargando actualización", "La actualización se está descargando en segundo plano...", NotificationType.Info);
                    await AppUpdaterService.DownloadAndApplyUpdateAsync(info.AssetDownloadUrl, baseDir);
                    Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
                }
            }
            else
            {
                DialogService.Instance.ShowMessage($"Cazador YTM está al día en su versión más reciente (v{Constants.AppVersion}).", "Cazador YTM Actualizado");
            }
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowMessage($"Error al consultar GitHub: {ex.Message}", "Error de Actualización", MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task CheckComponentsUpdate()
    {
        try
        {
            var baseDir = Helpers.GetBaseDir();
            var updatesNeeded = await Task.Run(() => BinaryUpdater.CheckUpdates(baseDir));
            if (updatesNeeded != null && updatesNeeded.Count > 0)
            {
                var details = string.Join("\n• ", updatesNeeded);
                var confirmMsg = $"Se encontraron nuevas versiones para los siguientes componentes:\n\n• {details}\n\n¿Desea descargarlos e instalarlos ahora?";
                if (DialogService.Instance.ShowConfirmation(confirmMsg, "Actualizaciones de Componentes"))
                {
                    NotificationService.Instance.Notify("Actualizando componentes", "Descargando e instalando componentes...", NotificationType.Info);
                    await Task.Run(() => BinaryUpdater.UpdateAll(baseDir));
                    NotificationService.Instance.Notify("Componentes Actualizados", "yt-dlp, FFmpeg y Deno se han actualizado correctamente.", NotificationType.Success);
                    DialogService.Instance.ShowMessage("Todos los componentes se han actualizado exitosamente.", "Actualización Completada");
                }
            }
            else
            {
                DialogService.Instance.ShowMessage("Todos los componentes (yt-dlp, FFmpeg, Deno) están en su versión más reciente.", "Componentes al día");
            }
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowMessage($"Error al actualizar componentes: {ex.Message}", "Error", MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenPreferences()
    {
        var win = new Views.PreferencesWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    [RelayCommand]
    private void OpenCookiesManager()
    {
        var win = new Views.CookiesManagerWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    [RelayCommand]
    private void ShowShortcuts()
    {
        var dlg = new Views.ShortcutsDialog
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    [RelayCommand]
    private void ExportLogs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar Registro de Errores de Cazador YTM",
            Filter = "Archivo de Registro (*.log)|*.log|Archivo de Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            FileName = $"cazador_registro_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };

        if (dialog.ShowDialog() == true)
        {
            if (LogService.Instance.ExportLogs(dialog.FileName))
            {
                NotificationService.Instance.Notify("Registro Exportado", $"Registro guardado exitosamente en:\n{dialog.FileName}", NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError("Exportar Registro", "No se pudo escribir el archivo de registro.");
            }
        }
    }

    [RelayCommand]
    private void ContactSupport()
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
    private void ShowAbout()
    {
        var msg = $"{Constants.AppTitle}\n\n" +
                  "Desarrollado por: narayan project's\n\n" +
                  "Gestor de Descargas y Reproductor Multimedia de Alto Rendimiento.\n" +
                  "Integración con yt-dlp, FFmpeg, Deno y YoutubeExplode.\n" +
                  "Arquitectura C# .NET 10 / WPF ModernWPF.\n\n" +
                  "Copyright © 2026 narayan project's. Todos los derechos reservados.";

        DialogService.Instance.ShowMessage(msg, "Acerca de Cazador YTM");
    }

    [RelayCommand]
    private void OpenDownloadsFolder()
    {
        var dir = DownloadVM.DestinationFolder;
        if (Directory.Exists(dir))
        {
            try
            {
                Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                DialogService.Instance.ShowMessage($"No se pudo abrir la carpeta: {ex.Message}", "Error");
            }
        }
    }
}
