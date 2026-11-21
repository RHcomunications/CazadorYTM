namespace CazadorYTM.Gui.Services;

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;

/// <summary>
/// Coordina la comprobación y aplicación de actualizaciones de la aplicación
/// y de los motores externos (yt-dlp, FFmpeg, Deno) desde un único punto para
/// evitar duplicación entre <see cref="ViewModels.MainViewModel"/> y
/// <see cref="ViewModels.PreferencesViewModel"/>.
/// </summary>
public static class UpdateCoordinatorService
{
    public static async Task CheckAppUpdateAsync(Action<string>? onStatusChanged = null)
    {
        var loc = LocalizationService.Instance;
        void Report(string status) => onStatusChanged?.Invoke(status);

        Report(loc["PrefStatusCheckingApp"]);

        try
        {
            var info = await AppUpdaterService.CheckAppUpdateAsync();
            if (info.HasUpdate)
            {
                Report(string.Format(loc["PrefStatusNewAppAvailable"], info.LatestVersion));
                var notesPreview = string.IsNullOrWhiteSpace(info.ReleaseNotes)
                    ? string.Empty
                    : string.Format(loc["MainMsgReleaseNotes"], info.ReleaseNotes);
                var confirmMsg = string.Format(loc["MainMsgNewVersion"], info.CurrentVersion, info.LatestVersion, notesPreview);

                var dialogTitle = onStatusChanged != null
                    ? loc["TitleAppUpdateAvailable"]
                    : loc["TitleAppUpdate"];

                if (DialogService.Instance.ShowConfirmation(confirmMsg, dialogTitle))
                {
                    if (string.IsNullOrEmpty(info.AssetDownloadUrl))
                    {
                        if (!string.IsNullOrEmpty(info.HtmlUrl))
                        {
                            Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
                        }
                        Report(loc["PrefStatusOpeningGitHub"]);
                        return;
                    }

                    Report(loc["PrefStatusDownloadingApp"]);
                    NotificationService.Instance.Notify(
                        loc.Get("TitleDownloadingUpdate", "Descargando actualización"),
                        loc.Get("MsgDownloadingUpdate", "La actualización se está descargando en segundo plano..."),
                        NotificationType.Info);

                    var baseDir = Helpers.GetBaseDir();
                    await AppUpdaterService.DownloadAndApplyUpdateAsync(
                        info.AssetDownloadUrl,
                        baseDir,
                        msg => DispatchStatus(Report, msg),
                        pct => DispatchStatus(Report, string.Format(loc["PrefStatusDownloadingAppPct"], pct)),
                        info.AssetSha256,
                        info.ChecksumDownloadUrl);

                    Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
                }
                else
                {
                    Report(loc["PrefStatusAppUpdatePostponed"]);
                }
            }
            else
            {
                Report(string.Format(loc["PrefStatusAppUpToDate"], Constants.AppVersion));
                if (onStatusChanged != null)
                {
                    DialogService.Instance.ShowMessage(
                        string.Format(loc["PrefMsgAppUpToDate"], Constants.AppVersion),
                        loc["TitleApplicationUpToDate"],
                        MessageBoxImage.Information);
                }
                else
                {
                    DialogService.Instance.ShowMessage(
                        string.Format(loc["MainMsgAppUpToDate"], Constants.AppVersion),
                        loc["TitleAppUpToDate"]);
                }
            }
        }
        catch (Exception ex)
        {
            Report(string.Format(loc["PrefStatusCheckAppError"], ex.Message));
            if (onStatusChanged != null)
            {
                DialogService.Instance.ShowMessage(
                    string.Format(loc["PrefMsgCheckReleasesFailed"], ex.Message),
                    loc["TitleError"],
                    MessageBoxImage.Error);
            }
            else
            {
                DialogService.Instance.ShowMessage(
                    string.Format(loc["MainMsgCheckGithubFailed"], ex.Message),
                    loc["TitleUpdateError"],
                    MessageBoxImage.Error);
            }
        }
    }

    public static async Task CheckComponentsUpdateAsync(
        Action<string>? onStatusChanged = null,
        bool showDetailedVersionsWhenUpToDate = false)
    {
        var loc = LocalizationService.Instance;
        void Report(string status) => onStatusChanged?.Invoke(status);

        Report(loc["PrefStatusCheckingEngines"]);

        try
        {
            var baseDir = Helpers.GetBaseDir();
            var updatesNeeded = await BinaryUpdater.CheckUpdatesAsync(baseDir);

            if (updatesNeeded != null && updatesNeeded.Count > 0)
            {
                var details = string.Join("\n• ", updatesNeeded);
                Report(string.Format(loc["PrefStatusUpdatesFound"], updatesNeeded.Count));

                var confirmMsg = onStatusChanged != null
                    ? string.Format(loc["PrefMsgComponentsFound"], details)
                    : string.Format(loc["MainMsgComponentsFound"], details);
                var confirmTitle = onStatusChanged != null
                    ? loc["TitleUpdatesAvailable"]
                    : loc["TitleComponentsUpdate"];

                if (DialogService.Instance.ShowConfirmation(confirmMsg, confirmTitle))
                {
                    Report(loc["PrefStatusDownloadingEngines"]);
                    NotificationService.Instance.Notify(
                        loc.Get("TitleUpdatingComponents", "Actualizando componentes"),
                        loc.Get("MsgUpdatingComponents", "Descargando e instalando componentes..."),
                        NotificationType.Info);

                    var updateResult = await Task.Run(() => BinaryUpdater.UpdateAll(
                        baseDir,
                        msg => DispatchStatus(Report, msg)));

                    if (updateResult.Success)
                    {
                        Report(loc["PrefStatusAllEnginesUpdated"]);
                        NotificationService.Instance.Notify(
                            loc["TitleComponentsUpdated"],
                            loc["MsgComponentsUpdatedSuccess"],
                            NotificationType.Success);
                        DialogService.Instance.ShowMessage(
                            loc["CommonMsgAllComponentsUpdated"],
                            loc["TitleUpdateCompleted"],
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        var failed = string.Join(", ", updateResult.Failed);
                        Report(string.Format(
                            loc.Get("PrefStatusEnginesUpdatePartial", "Actualización incompleta. Fallaron: {0}"),
                            failed));
                        NotificationService.Instance.NotifyWarning(
                            loc.Get("TitleUpdateIncomplete", "Actualización incompleta"),
                            string.Format(loc.Get("MsgComponentsUpdatePartial", "Fallaron: {0}"), failed));
                        DialogService.Instance.ShowMessage(
                            string.Format(
                                loc.Get("PrefMsgComponentsUpdatePartial", "No se pudieron actualizar los siguientes componentes: {0}\n\nRevisa el registro para más detalles."),
                                failed),
                            loc.Get("TitleUpdateIncomplete", "Actualización incompleta"),
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    Report(loc["PrefStatusEnginesUpdateCanceled"]);
                }
            }
            else
            {
                Report(loc["PrefStatusEnginesUpToDate"]);
                if (showDetailedVersionsWhenUpToDate)
                {
                    var localYtTask = Task.Run(() => BinaryUpdater.GetLocalYtdlpVersion(baseDir));
                    var localFfTask = Task.Run(() => BinaryUpdater.GetLocalFfmpegVersion(baseDir));
                    var localDenoTask = Task.Run(() => BinaryUpdater.GetLocalDenoVersion(baseDir));
                    await Task.WhenAll(localYtTask, localFfTask, localDenoTask);

                    var localYt = localYtTask.Result ?? "N/D";
                    var localFf = localFfTask.Result ?? "N/D";
                    var localDeno = localDenoTask.Result ?? "N/D";
                    DialogService.Instance.ShowMessage(
                        string.Format(loc["AdvMsgEngineUpToDate"], localYt, localFf, localDeno),
                        loc["TitleComponentsUpToDate"],
                        MessageBoxImage.Information);
                }
                else
                {
                    DialogService.Instance.ShowMessage(
                        loc["MainMsgComponentsUpToDate"],
                        loc["TitleComponentsUpToDate"]);
                }
            }
        }
        catch (Exception ex)
        {
            Report(string.Format(loc["PrefStatusCheckEnginesError"], ex.Message));
            if (onStatusChanged != null)
            {
                DialogService.Instance.ShowMessage(
                    string.Format(loc["PrefMsgVerifyUpdatesFailed"], ex.Message),
                    loc["TitleError"],
                    MessageBoxImage.Error);
            }
            else
            {
                DialogService.Instance.ShowMessage(
                    string.Format(loc["MainMsgUpdateComponentsFailed"], ex.Message),
                    loc["TitleError"],
                    MessageBoxImage.Error);
            }
        }
    }

    private static void DispatchStatus(Action<string> report, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => report(message));
        }
        else
        {
            report(message);
        }
    }
}
