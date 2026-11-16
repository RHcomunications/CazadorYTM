namespace CazadorYTM.Gui.Services;

using System;
using System.Diagnostics;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;

public static class AppActionsHelper
{
    public static void ExportLogs(string filePrefix = "cazador_registro")
    {
        var loc = LocalizationService.Instance;
        var path = DialogService.Instance.SaveFile(
            $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log",
            loc.Get("LogFilesFilter", "Archivo de Registro (*.log)|*.log|Archivo de Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*"),
            loc.Get("TitleExportErrorLog", "Exportar Registro de Errores de Cazador YTM"));

        if (string.IsNullOrEmpty(path)) return;

        if (LogService.Instance.ExportLogs(path))
        {
            NotificationService.Instance.Notify(
                loc.Get("TitleLogExported", "Registro Exportado"),
                string.Format(loc.Get("MsgLogExportedSuccess", "Registro guardado exitosamente en:\n{0}"), path),
                NotificationType.Success);
        }
        else
        {
            NotificationService.Instance.NotifyError(
                loc.Get("TitleLogExported", "Registro Exportado"),
                loc.Get("MsgLogExportFailed", "No se pudo escribir el archivo de registro en el destino seleccionado."));
        }
    }

    public static void ContactSupport()
    {
        var loc = LocalizationService.Instance;
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
            NotificationService.Instance.NotifyError(
                loc.Get("TitleTechSupport", "Soporte Técnico"),
                string.Format(loc.Get("MsgSupportLinkError", "No se pudo abrir el enlace de soporte: {0}"), ex.Message));
        }
    }
}
