namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class PreferencesViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _config;

    public LogsViewModel LogsVM { get; } = new();

    [ObservableProperty]
    private string _selectedBitrate = "320K";

    [ObservableProperty]
    private int _selectedConcurrent = 3;

    [ObservableProperty]
    private string _selectedBrowser = "Ninguno";

    [ObservableProperty]
    private bool _isSponsorBlock = true;

    [ObservableProperty]
    private bool _isNormalize = true;

    [ObservableProperty]
    private bool _isEmbedLyrics = false;

    [ObservableProperty]
    private bool _isEnumerate = false;

    [ObservableProperty]
    private bool _isClipboardMonitor = false;

    [ObservableProperty]
    private string _destinationFolder = string.Empty;

    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Automático";

    [ObservableProperty]
    private string _selectedLanguage = "Español";

    [ObservableProperty]
    private bool _isCheckingUpdates = false;

    [ObservableProperty]
    private string _updateStatusText = "Listo para comprobar herramientas";

    [ObservableProperty]
    private bool _isVerifying = false;

    [ObservableProperty]
    private string _verifyStatusText = "Listo para verificar";

    public ObservableCollection<string> Bitrates { get; } = new(Constants.Bitrates);
    public ObservableCollection<int> ConcurrencyOptions { get; } = new(Constants.MaxConcurrentValues);
    public ObservableCollection<string> Browsers { get; } = new(Constants.Browsers);
    public ObservableCollection<string> ThemeOptions { get; } = new(Constants.ThemeOptions);
    public ObservableCollection<string> LanguageOptions { get; } = new(Constants.LanguageOptions);
    public ObservableCollection<string> StartupTabOptions { get; } = new(Constants.StartupTabOptions);

    [ObservableProperty]
    private string _selectedStartupTab = "Descarga";

    public event Action? RequestClose;

    public PreferencesViewModel()
    {
        _config = new AppConfig();

        _selectedBitrate = _config.Get("bitrate", "320K");
        _selectedConcurrent = _config.Get("max_concurrent", 3);
        _selectedBrowser = _config.Get("cookies_browser", "Ninguno");
        _isSponsorBlock = _config.Get("sponsorblock", true);
        _isNormalize = _config.Get("normalize", true);
        _isEmbedLyrics = _config.Get("embed_lyrics", false);
        _isEnumerate = _config.Get("enumerar", false);
        _isClipboardMonitor = _config.Get("clipboard_monitor", false);
        _proxyUrl = _config.Get("proxy", "");
        _selectedTheme = _config.Get("theme", "Automático");

        var langCode = _config.Get("language", "es");
        _selectedLanguage = langCode.Equals("en", StringComparison.OrdinalIgnoreCase) ? "English" : "Español";

        var savedDest = _config.Get("destination_folder", "");
        _destinationFolder = string.IsNullOrWhiteSpace(savedDest) || !Directory.Exists(savedDest)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Constants.MultiDownloadFolder)
            : savedDest;

        _selectedStartupTab = _config.Get("startup_tab", "Descarga");
        if (!Constants.StartupTabOptions.Contains(_selectedStartupTab))
            _selectedStartupTab = "Descarga";
    }

    [RelayCommand]
    private void SelectDestinationFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta predeterminada de descargas",
            InitialDirectory = Directory.Exists(DestinationFolder) ? DestinationFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        if (dialog.ShowDialog() == true)
        {
            DestinationFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task CheckAppUpdate()
    {
        var loc = LocalizationService.Instance;
        IsCheckingUpdates = true;
        UpdateStatusText = loc["PrefStatusCheckingApp"];

        try
        {
            var info = await AppUpdaterService.CheckAppUpdateAsync();
            if (info.HasUpdate)
            {
                UpdateStatusText = string.Format(loc["PrefStatusNewAppAvailable"], info.LatestVersion);
                var notesPreview = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "" : string.Format(loc["MainMsgReleaseNotes"], info.ReleaseNotes);
                var confirmMsg = string.Format(loc["MainMsgNewVersion"], info.CurrentVersion, info.LatestVersion, notesPreview);

                var accept = DialogService.Instance.ShowConfirmation(confirmMsg, loc["TitleAppUpdateAvailable"]);
                if (accept)
                {
                    if (string.IsNullOrEmpty(info.AssetDownloadUrl))
                    {
                        // Open release page if no direct zip asset was found
                        if (!string.IsNullOrEmpty(info.HtmlUrl))
                        {
                            Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
                        }
                        UpdateStatusText = loc["PrefStatusOpeningGitHub"];
                        return;
                    }

                    UpdateStatusText = loc["PrefStatusDownloadingApp"];
                    var baseDir = Helpers.GetBaseDir();
                    await AppUpdaterService.DownloadAndApplyUpdateAsync(
                        info.AssetDownloadUrl,
                        baseDir,
                        msg => Application.Current?.Dispatcher.Invoke(() => UpdateStatusText = msg),
                        pct => Application.Current?.Dispatcher.Invoke(() => UpdateStatusText = string.Format(loc["PrefStatusDownloadingAppPct"], pct)),
                        info.AssetSha256,
                        info.ChecksumDownloadUrl);

                    Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
                }
                else
                {
                    UpdateStatusText = loc["PrefStatusAppUpdatePostponed"];
                }
            }
            else
            {
                UpdateStatusText = string.Format(loc["PrefStatusAppUpToDate"], Constants.AppVersion);
                DialogService.Instance.ShowMessage(
                    string.Format(loc["PrefMsgAppUpToDate"], Constants.AppVersion),
                    loc["TitleApplicationUpToDate"],
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = string.Format(loc["PrefStatusCheckAppError"], ex.Message);
            DialogService.Instance.ShowMessage(string.Format(loc["PrefMsgCheckReleasesFailed"], ex.Message), loc["TitleError"], MessageBoxImage.Error);
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        var loc = LocalizationService.Instance;
        IsCheckingUpdates = true;
        UpdateStatusText = loc["PrefStatusCheckingEngines"];

        try
        {
            var baseDir = Helpers.GetBaseDir();
            var updatesNeeded = await Task.Run(() => BinaryUpdater.CheckUpdates(baseDir));

            if (updatesNeeded != null && updatesNeeded.Count > 0)
            {
                var details = string.Join("\n• ", updatesNeeded);
                UpdateStatusText = string.Format(loc["PrefStatusUpdatesFound"], updatesNeeded.Count);

                var confirmMessage = string.Format(loc["PrefMsgComponentsFound"], details);
                var accept = DialogService.Instance.ShowConfirmation(confirmMessage, loc["TitleUpdatesAvailable"]);

                if (accept)
                {
                    UpdateStatusText = loc["PrefStatusDownloadingEngines"];
                    await Task.Run(() => BinaryUpdater.UpdateAll(baseDir, msg =>
                    {
                        Application.Current?.Dispatcher.Invoke(() => UpdateStatusText = msg);
                    }));

                    UpdateStatusText = loc["PrefStatusAllEnginesUpdated"];
                    DialogService.Instance.ShowMessage(loc["CommonMsgAllComponentsUpdated"], loc["TitleUpdateCompleted"], MessageBoxImage.Information);
                    NotificationService.Instance.Notify(loc["TitleComponentsUpdated"], loc["MsgComponentsUpdatedSuccess"], NotificationType.Success);
                }
                else
                {
                    UpdateStatusText = loc["PrefStatusEnginesUpdateCanceled"];
                }
            }
            else
            {
                var localYt = BinaryUpdater.GetLocalYtdlpVersion(baseDir) ?? "N/D";
                var localFf = BinaryUpdater.GetLocalFfmpegVersion(baseDir) ?? "N/D";
                var localDeno = BinaryUpdater.GetLocalDenoVersion(baseDir) ?? "N/D";

                UpdateStatusText = loc["PrefStatusEnginesUpToDate"];
                DialogService.Instance.ShowMessage(
                    string.Format(loc["AdvMsgEngineUpToDate"], localYt, localFf, localDeno),
                    loc["TitleComponentsUpToDate"],
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = string.Format(loc["PrefStatusCheckEnginesError"], ex.Message);
            DialogService.Instance.ShowMessage(string.Format(loc["PrefMsgVerifyUpdatesFailed"], ex.Message), loc["TitleError"], MessageBoxImage.Error);
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task RunVerify()
    {
        var loc = LocalizationService.Instance;
        IsVerifying = true;
        VerifyStatusText = loc["PrefStatusVerifyingAudio"];

        try
        {
            var targetFolder = DestinationFolder;
            if (!Directory.Exists(targetFolder))
            {
                VerifyStatusText = loc["PrefStatusTargetFolderNotFound"];
                return;
            }

            var results = await Task.Run(() => FileVerifier.VerificarDirectorio(targetFolder, msg =>
            {
                Application.Current.Dispatcher.Invoke(() => VerifyStatusText = msg);
            }));

            var (ok, warn, err) = Helpers.SummarizeResults(results);
            VerifyStatusText = string.Format(loc["PrefStatusVerifySummary"], ok, warn, err);

            NotificationService.Instance.NotifyVerificationComplete(results.Count, err);
        }
        catch (Exception ex)
        {
            VerifyStatusText = $"Error al verificar: {ex.Message}";
            NotificationService.Instance.NotifyError(loc["TitleVerificationError"], ex.Message);
        }
        finally
        {
            IsVerifying = false;
        }
    }

    [RelayCommand]
    private void OpenCookiesManager()
    {
        var win = new Views.CookiesManagerWindow
        {
            Owner = Application.Current.Windows.OfType<Views.PreferencesWindow>().FirstOrDefault()
        };
        win.ShowDialog();

        var savedBrowser = _config.Get("cookies_browser", "Ninguno");
        if (Browsers.Contains(savedBrowser))
        {
            SelectedBrowser = savedBrowser;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _config.Set("bitrate", SelectedBitrate);
        _config.Set("max_concurrent", SelectedConcurrent);
        _config.Set("cookies_browser", SelectedBrowser);
        _config.Set("sponsorblock", IsSponsorBlock);
        _config.Set("normalize", IsNormalize);
        _config.Set("embed_lyrics", IsEmbedLyrics);
        _config.Set("enumerar", IsEnumerate);
        _config.Set("clipboard_monitor", IsClipboardMonitor);
        _config.Set("destination_folder", DestinationFolder);
        _config.Set("proxy", ProxyUrl);
        _config.Set("theme", SelectedTheme);
        _config.Set("startup_tab", SelectedStartupTab);

        var prevLang = _config.Get("language", "es");
        var langCode = SelectedLanguage.Equals("English", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
        var languageChanged = !prevLang.Equals(langCode, StringComparison.OrdinalIgnoreCase);

        _config.Set("language", langCode);
        LocalizationService.Instance.SetLanguage(langCode);

        _config.Save();

        if (SelectedTheme.Equals("Oscuro", StringComparison.OrdinalIgnoreCase))
        {
            ModernWpf.ThemeManager.Current.ApplicationTheme = ModernWpf.ApplicationTheme.Dark;
        }
        else if (SelectedTheme.Equals("Claro", StringComparison.OrdinalIgnoreCase))
        {
            ModernWpf.ThemeManager.Current.ApplicationTheme = ModernWpf.ApplicationTheme.Light;
        }
        else
        {
            ModernWpf.ThemeManager.Current.ApplicationTheme = null;
        }

        if (Application.Current?.MainWindow != null)
        {
            Win11ThemeHelper.ApplyWin11Backdrop(Application.Current.MainWindow, isDialog: false);
        }

        RequestClose?.Invoke();

        if (languageChanged)
        {
            var msg = LocalizationService.Instance["PrefLanguageChangedMsg"];
            var title = LocalizationService.Instance["PrefLanguageChangedTitle"];

            if (DialogService.Instance.ShowConfirmation(msg, title))
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = processPath,
                        UseShellExecute = true
                    });
                }
                Application.Current?.Shutdown();
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }

    public void Dispose()
    {
        LogsVM.Dispose();
    }
}
