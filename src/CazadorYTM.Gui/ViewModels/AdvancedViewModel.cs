namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class AdvancedViewModel : ObservableObject
{
    private readonly AppConfig _config;

    [ObservableProperty]
    private string _verifyFolderPath = string.Empty;

    [ObservableProperty]
    private bool _isDeepVerify = false;

    [ObservableProperty]
    private bool _isVerifying = false;

    [ObservableProperty]
    private double _verifyProgress = 0;

    [ObservableProperty]
    private string _verifyStatus = "Listo para verificar";

    [ObservableProperty]
    private string _verifySummary = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdates = false;

    [ObservableProperty]
    private bool _isUpdating = false;

    [ObservableProperty]
    private double _updateProgress = 0;

    [ObservableProperty]
    private string _updateStatus = "Binarios al día";

    [ObservableProperty]
    private string _proxyText = string.Empty;

    public ObservableCollection<ResultadoArchivo> VerificationResults { get; } = new();
    public ObservableCollection<string> UpdatesNeeded { get; } = new();

    public AdvancedViewModel()
    {
        _config = new AppConfig();
        _proxyText = _config.Get("proxy", "");
        _verifyFolderPath = _config.Get("destination_folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Constants.MultiDownloadFolder));
    }

    [RelayCommand]
    private void SelectVerifyFolder()
    {
        var folder = DialogService.Instance.SelectFolder(VerifyFolderPath);
        if (!string.IsNullOrEmpty(folder))
        {
            VerifyFolderPath = folder;
        }
    }

    [RelayCommand]
    private async Task StartVerification()
    {
        if (string.IsNullOrWhiteSpace(VerifyFolderPath) || !Directory.Exists(VerifyFolderPath))
        {
            DialogService.Instance.ShowMessage("Por favor seleccione una carpeta válida existente.", "Aviso", MessageBoxImage.Warning);
            return;
        }

        IsVerifying = true;
        VerificationResults.Clear();
        VerifyProgress = 0;
        VerifyStatus = "Analizando archivos de audio con FFprobe...";
        VerifySummary = string.Empty;

        try
        {
            var results = await Task.Run(() => FileVerifier.VerificarDirectorio(
                VerifyFolderPath,
                loggerCallback: msg => LogService.Instance.Info(msg),
                progressCallback: (pct, detail) =>
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        VerifyProgress = pct;
                        VerifyStatus = $"Verificando: {detail} ({pct}%)";
                    });
                },
                deepVerify: IsDeepVerify));

            foreach (var r in results)
            {
                VerificationResults.Add(r);
            }

            var (ok, warn, err) = Helpers.SummarizeResults(results);
            VerifySummary = $"Total: {results.Count} | Correctos: {ok} ✅ | Avisos: {warn} ⚠️ | Errores: {err} ❌";
            VerifyStatus = "Verificación completada.";
            VerifyProgress = 100;
        }
        catch (Exception ex)
        {
            VerifyStatus = $"Error: {ex.Message}";
            LogService.Instance.Error($"Error en verificación: {ex.Message}");
        }
        finally
        {
            IsVerifying = false;
        }
    }

    [RelayCommand]
    private void OpenReport()
    {
        var reportPath = Path.Combine(VerifyFolderPath, "_REPORTE_INTEGRIDAD.txt");
        if (File.Exists(reportPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DialogService.Instance.ShowMessage($"No se pudo abrir el archivo de reporte: {ex.Message}", "Error", MessageBoxImage.Error);
            }
        }
        else
        {
            DialogService.Instance.ShowMessage("Aún no se ha generado un reporte de integridad en esta carpeta.", "Aviso", MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        IsCheckingUpdates = true;
        UpdateStatus = "Consultando versiones más recientes en GitHub...";
        UpdatesNeeded.Clear();

        try
        {
            var baseDir = Helpers.GetBaseDir();
            var result = await Task.Run(() => BinaryUpdater.CheckUpdates(baseDir));
            foreach (var item in result)
            {
                UpdatesNeeded.Add(item);
            }

            if (UpdatesNeeded.Count == 0)
            {
                var localYt = BinaryUpdater.GetLocalYtdlpVersion(baseDir) ?? "N/D";
                var localFf = BinaryUpdater.GetLocalFfmpegVersion(baseDir) ?? "N/D";
                var localDeno = BinaryUpdater.GetLocalDenoVersion(baseDir) ?? "N/D";

                UpdateStatus = "Todos los binarios (yt-dlp, FFmpeg, Deno) están en su última versión.";
                DialogService.Instance.ShowMessage(
                    $"Todos los componentes del motor están al día:\n\n• yt-dlp: {localYt}\n• FFmpeg: {localFf}\n• Deno: {localDeno}",
                    "Componentes al día",
                    MessageBoxImage.Information);
            }
            else
            {
                UpdateStatus = $"Se encontraron {UpdatesNeeded.Count} actualizaciones disponibles.";
                var details = string.Join("\n• ", UpdatesNeeded);
                var confirmMessage = $"Se encontraron nuevas versiones para los siguientes componentes:\n\n• {details}\n\n¿Desea descargarlas e instalarlas ahora mismo?";
                var accept = DialogService.Instance.ShowConfirmation(confirmMessage, "Actualizaciones Disponibles");

                if (accept)
                {
                    IsUpdating = true;
                    UpdateStatus = "Iniciando descarga e instalación de componentes...";
                    UpdateProgress = 0;

                    await Task.Run(() => BinaryUpdater.UpdateAll(
                        baseDir,
                        callback: msg => LogService.Instance.Info(msg),
                        progressCallback: pct =>
                        {
                            Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                UpdateProgress = pct;
                                UpdateStatus = $"Actualizando componentes ({pct}%)...";
                            });
                        }));

                    UpdateStatus = "¡Todos los componentes se actualizaron exitosamente!";
                    UpdateProgress = 100;
                    UpdatesNeeded.Clear();
                    DialogService.Instance.ShowMessage("La actualización de binarios finalizó correctamente.", "Éxito", MessageBoxImage.Information);
                    NotificationService.Instance.Notify("Componentes Actualizados", "yt-dlp, FFmpeg y Deno se han actualizado a su versión más reciente.", NotificationType.Success);
                }
                else
                {
                    UpdateStatus = "Actualización cancelada por el usuario.";
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Error al buscar actualizaciones: {ex.Message}";
            LogService.Instance.Error($"Error al verificar actualizaciones: {ex.Message}");
            DialogService.Instance.ShowMessage($"Error al consultar actualizaciones: {ex.Message}", "Error", MessageBoxImage.Error);
        }
        finally
        {
            IsCheckingUpdates = false;
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void SaveProxy()
    {
        _config.Set("proxy", ProxyText.Trim());
        _config.Save();
        LogService.Instance.Info($"Configuración de proxy guardada: {(string.IsNullOrEmpty(ProxyText) ? "Ninguno" : ProxyText)}");
        DialogService.Instance.ShowMessage("Configuración de Proxy guardada.", "Guardado");
    }
}
