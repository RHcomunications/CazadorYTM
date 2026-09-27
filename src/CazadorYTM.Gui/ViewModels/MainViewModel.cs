namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class MainViewModel : ObservableObject
{
    public DownloadViewModel DownloadVM { get; }
    public PlayerViewModel PlayerVM { get; }
    public HistoryViewModel HistoryVM { get; }
    public LogsViewModel LogsVM { get; }

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    [ObservableProperty]
    private string _statusText = "Listo";

    public string AppTitle => $"{Constants.AppTitle}";

    public MainViewModel()
    {
        DownloadVM = new DownloadViewModel();
        PlayerVM = new PlayerViewModel();
        HistoryVM = new HistoryViewModel();
        LogsVM = new LogsViewModel();

        LogService.Instance.Info($"{Constants.AppTitle} iniciado correctamente.");

        _ = CheckForUpdatesSilentlyAsync();
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
    private void NavigateToTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out var idx) && idx >= 0 && idx <= 2)
        {
            SelectedTabIndex = idx;
            if (idx == 1)
            {
                HistoryVM.RefreshHistory();
            }
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
    private void ShowShortcuts()
    {
        var dlg = new Views.ShortcutsDialog
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
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
