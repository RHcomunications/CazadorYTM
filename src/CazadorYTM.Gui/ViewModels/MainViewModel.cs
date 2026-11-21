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

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _config;
    private bool _disposed;

    public DownloadViewModel DownloadVM { get; }
    public PlayerViewModel PlayerVM { get; }
    public HistoryViewModel HistoryVM { get; }
    public RadarViewModel RadarVM { get; }

    public ObservableCollection<MainTabItem> OpenTabs { get; } = new();

    [ObservableProperty]
    private MainTabItem? _selectedTab;

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

        DownloadVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DownloadVM.IsBusy) ||
                e.PropertyName == nameof(DownloadVM.ProgressPercentage) ||
                e.PropertyName == nameof(DownloadVM.CurrentPhase))
            {
                UpdateTaskbarProgress();
            }
        };

        HistoryVM.RetryRequested += (entry) =>
        {
            OpenOrFocusTab(MainTabType.Downloads);
            var target = !string.IsNullOrWhiteSpace(entry.Url) ? entry.Url : entry.Title ?? "";
            DownloadVM.InputText = target;
            var loc = LocalizationService.Instance;
            NotificationService.Instance.Notify(loc.Get("TitleRetryDownload", "Reintentar Descarga"), string.Format(loc.Get("MsgRetryDownloadLoaded", "Canción cargada en el panel de descargas: {0}"), target), NotificationType.Info);
        };

        RadarVM.SendToDownloadRequested += (entry) =>
        {
            OpenOrFocusTab(MainTabType.Downloads);
            var target = !string.IsNullOrWhiteSpace(entry.Url) ? entry.Url : entry.Title ?? "";
            DownloadVM.SetInputFromRadar(target);
            if (!DownloadVM.IsBusy)
            {
                _ = DownloadVM.SubmitInputCommand.ExecuteAsync(null);
            }
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
        if (startup.Equals("Biblioteca", StringComparison.OrdinalIgnoreCase))
        {
            OpenOrFocusTab(MainTabType.History);
        }
        else if (startup.Equals("Reproductor", StringComparison.OrdinalIgnoreCase))
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

        if (DownloadVM.CurrentPhase == DownloadTaskPhase.Error)
        {
            TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
            return;
        }

        if (DownloadVM.CurrentPhase == DownloadTaskPhase.Analyzing ||
            DownloadVM.CurrentPhase == DownloadTaskPhase.PostProcessing ||
            DownloadVM.ProgressPercentage <= 0.0)
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
                var loc = LocalizationService.Instance;
                NotificationService.Instance.Notify(
                    loc.Get("TitleNewVersionAvailable", "Nueva versión disponible"),
                    string.Format(loc.Get("MsgNewVersionAvailable", "Cazador YTM v{0} está disponible para descargar."), info.LatestVersion),
                    NotificationType.Info);
            }
        }
        catch { }
    }

    [RelayCommand]
    private Task CheckAppUpdate() => UpdateCoordinatorService.CheckAppUpdateAsync();

    [RelayCommand]
    private Task CheckComponentsUpdate() => UpdateCoordinatorService.CheckComponentsUpdateAsync();

    [RelayCommand]
    private void OpenPreferences()
    {
        var win = new Views.PreferencesWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
        DownloadVM.ReloadPreferences();
        RadarVM.HasSessionConnected = RadarVM.CheckSessionConnected();
    }

    [RelayCommand]
    private void OpenCookiesManager()
    {
        var win = new Views.CookiesManagerWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
        DownloadVM.ReloadPreferences();
        RadarVM.HasSessionConnected = RadarVM.CheckSessionConnected();
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
    private void ExportLogs() => AppActionsHelper.ExportLogs();

    [RelayCommand]
    private void ContactSupport() => AppActionsHelper.ContactSupport();

    [RelayCommand]
    private void ShowAbout()
    {
        var msg = string.Format(LocalizationService.Instance["MainAboutMsg"], Constants.AppTitle);

        DialogService.Instance.ShowMessage(msg, LocalizationService.Instance["TitleAbout"]);
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
                DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["CommonMsgOpenFolderFailed"], ex.Message), LocalizationService.Instance["TitleError"]);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DownloadVM.Dispose();
        HistoryVM.Dispose();
        PlayerVM.Dispose();
        RadarVM.Dispose();
    }
}
