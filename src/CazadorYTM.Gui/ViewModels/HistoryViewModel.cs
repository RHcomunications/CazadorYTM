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

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryManager _historyManager;

    public event Action<DownloadEntry>? RetryRequested;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private DownloadEntry? _selectedEntry;

    public ObservableCollection<DownloadEntry> HistoryEntries { get; } = new();
    public ObservableCollection<DownloadEntry> FilteredEntries { get; } = new();

    public HistoryViewModel()
    {
        _historyManager = new HistoryManager();
        RefreshHistory();

        HistoryManager.GlobalHistoryChanged += () =>
        {
            Application.Current?.Dispatcher.InvokeAsync(RefreshHistory);
        };
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public void RefreshHistory()
    {
        HistoryEntries.Clear();
        var entries = _historyManager.GetEntries();
        foreach (var entry in entries)
        {
            HistoryEntries.Add(entry);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var query = SearchFilter.Trim().ToLower();
        var items = string.IsNullOrEmpty(query)
            ? HistoryEntries
            : HistoryEntries.Where(e => (e.Title ?? "").ToLower().Contains(query) ||
                                        (e.FilePath ?? "").ToLower().Contains(query));

        foreach (var item in items)
        {
            FilteredEntries.Add(item);
        }

        if (SelectedEntry == null || !FilteredEntries.Contains(SelectedEntry))
        {
            SelectedEntry = FilteredEntries.FirstOrDefault();
        }
    }

    private string? ResolveActualFilePath(DownloadEntry target)
    {
        if (!string.IsNullOrEmpty(target.FilePath) && File.Exists(target.FilePath))
        {
            return target.FilePath;
        }

        // Try searching in configured destination folder
        var config = new AppConfig();
        var destFolder = config.Get("destination_folder", "");
        if (string.IsNullOrEmpty(destFolder) || !Directory.Exists(destFolder))
        {
            destFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Constants.MultiDownloadFolder);
        }

        if (Directory.Exists(destFolder))
        {
            if (!string.IsNullOrEmpty(target.FilePath))
            {
                var fname = Path.GetFileName(target.FilePath);
                var test = Path.Combine(destFolder, fname);
                if (File.Exists(test)) return test;
            }

            if (!string.IsNullOrEmpty(target.Title))
            {
                try
                {
                    var matches = Directory.GetFiles(destFolder, $"*{target.Title}*");
                    if (matches.Length > 0) return matches[0];
                }
                catch { }
            }
        }

        return target.FilePath;
    }

    [RelayCommand]
    public void PlaySelected(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target == null)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgSelectToPlay"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var realPath = ResolveActualFilePath(target);
        if (!string.IsNullOrEmpty(realPath) && File.Exists(realPath))
        {
            // Populate the queue with all currently displayed/filtered library tracks
            var allTracks = FilteredEntries
                .Select(ResolveActualFilePath)
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Cast<string>()
                .ToList();

            var targetIdx = allTracks.IndexOf(realPath);
            if (targetIdx >= 0)
            {
                AudioPlayerService.Instance.SetQueue(allTracks, targetIdx);
            }
            else
            {
                AudioPlayerService.Instance.Play(realPath);
            }

            NotificationService.Instance.Notify("Reproductor", $"Reproduciendo: {target.Title}", NotificationType.Info);
        }
        else
        {
            DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["HistoryMsgFileMissing"], target.Title, target.FilePath), LocalizationService.Instance["TitleWarning"], MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public void PlayAll()
    {
        var allTracks = FilteredEntries
            .Select(ResolveActualFilePath)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Cast<string>()
            .ToList();

        if (allTracks.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNoAudioToPlay"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        AudioPlayerService.Instance.SetQueue(allTracks, 0);
        NotificationService.Instance.Notify("Reproductor", $"Reproduciendo toda la biblioteca ({allTracks.Count} pistas)", NotificationType.Info);
    }

    [RelayCommand]
    public void ExportPlaylistM3U8()
    {
        var allTracks = FilteredEntries
            .Select(e => (Entry: e, Path: ResolveActualFilePath(e)))
            .Where(t => !string.IsNullOrEmpty(t.Path) && File.Exists(t.Path))
            .ToList();

        if (allTracks.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNothingToExport"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var savePath = DialogService.Instance.SaveFile("Cazador_Biblioteca.m3u8", "Lista de reproducción M3U8 (*.m3u8)|*.m3u8|Lista M3U (*.m3u)|*.m3u");
        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var item in allTracks)
            {
                sb.AppendLine($"#EXTINF:-1,{item.Entry.Title ?? Path.GetFileNameWithoutExtension(item.Path)}");
                sb.AppendLine(item.Path);
            }

            File.WriteAllText(savePath, sb.ToString(), System.Text.Encoding.UTF8);
            NotificationService.Instance.Notify("Lista Exportada", $"Se exportaron {allTracks.Count} canciones a la lista M3U8.", NotificationType.Success);
            DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["HistoryMsgExportDone"], savePath), LocalizationService.Instance["TitleExportDone"]);
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["HistoryMsgExportFailed"], ex.Message), LocalizationService.Instance["TitleError"], MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void OpenContainingFolder(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target == null)
        {
            var config = new AppConfig();
            var dest = config.Get("destination_folder", "");
            if (Directory.Exists(dest))
            {
                Process.Start("explorer.exe", dest);
            }
            return;
        }

        var realPath = ResolveActualFilePath(target);
        if (!string.IsNullOrEmpty(realPath) && File.Exists(realPath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{realPath}\"");
            }
            catch (Exception ex)
            {
                DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["CommonMsgOpenFolderFailed"], ex.Message), LocalizationService.Instance["TitleError"], MessageBoxImage.Error);
            }
        }
        else
        {
            var config = new AppConfig();
            var dest = config.Get("destination_folder", "");
            if (Directory.Exists(dest))
            {
                Process.Start("explorer.exe", dest);
            }
            else
            {
                DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgDownloadsFolderNotFound"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            }
        }
    }

    [RelayCommand]
    public void CopyUrl(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target != null && !string.IsNullOrEmpty(target.Url))
        {
            Clipboard.SetText(target.Url);
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgLinkCopied"], LocalizationService.Instance["TitleInfo"], MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    public void DeleteSelected(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target != null)
        {
            if (DialogService.Instance.ShowConfirmation(string.Format(LocalizationService.Instance["HistoryMsgConfirmDelete"], target.Title), LocalizationService.Instance["TitleConfirmDeletion"]))
            {
                _historyManager.RemoveEntry(target);
                RefreshHistory();
            }
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        if (DialogService.Instance.ShowConfirmation(LocalizationService.Instance["HistoryMsgConfirmClearAll"], LocalizationService.Instance["TitleClearHistory"]))
        {
            _historyManager.Clear();
            RefreshHistory();
            LogService.Instance.Info("Historial de descargas vaciado.");
        }
    }

    [RelayCommand]
    public void RetryDownload(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target == null)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgSelectToRetry"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        RetryRequested?.Invoke(target);
    }

    [RelayCommand]
    public void CleanMissingEntries()
    {
        var all = HistoryEntries.ToList();
        var missing = all.Where(e =>
        {
            var path = ResolveActualFilePath(e);
            return string.IsNullOrEmpty(path) || !File.Exists(path);
        }).ToList();

        if (missing.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgLibraryClean"], LocalizationService.Instance["TitleLibraryClean"], MessageBoxImage.Information);
            return;
        }

        var confirm = DialogService.Instance.ShowConfirmation(
            string.Format(LocalizationService.Instance["HistoryMsgConfirmCleanMissing"], missing.Count),
            LocalizationService.Instance["TitleCleanLibrary"]);

        if (confirm)
        {
            var removed = _historyManager.RemoveEntries(missing);
            RefreshHistory();
            NotificationService.Instance.Notify("Biblioteca Depurada", $"Se eliminaron {removed} registros obsoletos.", NotificationType.Success);
            LogService.Instance.Info($"Depuración de biblioteca: {removed} registros removidos.");
        }
    }
}
