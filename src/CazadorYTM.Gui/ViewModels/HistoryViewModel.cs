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

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly HistoryManager _historyManager;
    private bool _disposed;

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

        HistoryManager.GlobalHistoryChanged += OnGlobalHistoryChanged;
    }

    private void OnGlobalHistoryChanged()
    {
        if (_disposed) return;
        Application.Current?.Dispatcher.InvokeAsync(RefreshHistory);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HistoryManager.GlobalHistoryChanged -= OnGlobalHistoryChanged;
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

    private static readonly EnumerationOptions SafeSearchOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true
    };

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var query = SearchFilter.Trim();
        var items = string.IsNullOrEmpty(query)
            ? HistoryEntries
            : HistoryEntries.Where(e => (e.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                        (e.FilePath ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));

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

        // Try searching in target's recorded folder or configured destination folder
        var searchFolders = new System.Collections.Generic.List<string>(2);
        if (!string.IsNullOrEmpty(target.FilePath) && Directory.Exists(target.FilePath))
        {
            searchFolders.Add(target.FilePath);
        }

        var config = new AppConfig();
        var destFolder = config.Get("destination_folder", "");
        if (string.IsNullOrEmpty(destFolder) || !Directory.Exists(destFolder))
        {
            destFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Constants.MultiDownloadFolder);
        }

        if (Directory.Exists(destFolder) && !searchFolders.Contains(destFolder, StringComparer.OrdinalIgnoreCase))
        {
            searchFolders.Add(destFolder);
        }

        foreach (var folder in searchFolders)
        {
            if (!string.IsNullOrEmpty(target.FilePath) && !Directory.Exists(target.FilePath))
            {
                var fname = Path.GetFileName(target.FilePath);
                if (!string.IsNullOrEmpty(fname))
                {
                    var test = Path.Combine(folder, fname);
                    if (File.Exists(test)) return test;
                }
            }

            if (!string.IsNullOrWhiteSpace(target.Title))
            {
                try
                {
                    var sanitizedTitle = Helpers.SanitizeFilename(target.Title);
                    if (!string.IsNullOrWhiteSpace(sanitizedTitle))
                    {
                        var match = Directory.EnumerateFiles(folder, $"*{sanitizedTitle}*", SafeSearchOptions).FirstOrDefault();
                        if (match != null) return match;
                    }
                }
                catch { }
            }
        }

        return target.FilePath;
    }

    [RelayCommand]
    public async Task PlaySelectedAsync(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target == null)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgSelectToPlay"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var realPath = await Task.Run(() => ResolveActualFilePath(target));
        if (string.IsNullOrEmpty(realPath) || !File.Exists(realPath))
        {
            DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["HistoryMsgFileMissing"], target.Title, target.FilePath), LocalizationService.Instance["TitleWarning"], MessageBoxImage.Warning);
            return;
        }

        // Iniciar reproducción de la pista seleccionada de inmediato
        AudioPlayerService.Instance.Play(realPath);
        NotificationService.Instance.Notify(LocalizationService.Instance["TitlePlayer"], string.Format(LocalizationService.Instance["MsgPlayingTrack"], target.Title), NotificationType.Info);

        // Construir la cola completa en segundo plano sin bloquear la interfaz
        var snapshot = FilteredEntries.ToList();
        var allTracks = await Task.Run(() =>
        {
            return snapshot
                .Select(ResolveActualFilePath)
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Cast<string>()
                .ToList();
        });

        var targetIdx = allTracks.IndexOf(realPath);
        if (targetIdx >= 0)
        {
            if (AudioPlayerService.Instance.CurrentFilePath == realPath)
            {
                AudioPlayerService.Instance.UpdateQueueWithoutRestarting(allTracks, targetIdx);
            }
        }
    }

    public void PlaySelected(DownloadEntry? entry = null) => _ = PlaySelectedAsync(entry);

    [RelayCommand]
    public async Task PlayAllAsync()
    {
        var snapshot = FilteredEntries.ToList();
        if (snapshot.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNoAudioToPlay"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var allTracks = await Task.Run(() =>
        {
            return snapshot
                .Select(ResolveActualFilePath)
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Cast<string>()
                .ToList();
        });

        if (allTracks.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNoAudioToPlay"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        AudioPlayerService.Instance.SetQueue(allTracks, 0);
        NotificationService.Instance.Notify(LocalizationService.Instance["TitlePlayer"], string.Format(LocalizationService.Instance["MsgPlayingAllLibrary"], allTracks.Count), NotificationType.Info);
    }

    public void PlayAll() => _ = PlayAllAsync();

    [RelayCommand]
    public async Task ExportPlaylistM3U8Async()
    {
        var snapshot = FilteredEntries.ToList();
        if (snapshot.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNothingToExport"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var allTracks = await Task.Run(() =>
        {
            return snapshot
                .Select(e => (Entry: e, Path: ResolveActualFilePath(e)))
                .Where(t => !string.IsNullOrEmpty(t.Path) && File.Exists(t.Path))
                .ToList();
        });

        if (allTracks.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNothingToExport"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var savePath = DialogService.Instance.SaveFile("Cazador_Biblioteca.m3u8", LocalizationService.Instance["M3u8FilesFilter"]);
        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            await Task.Run(() =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("#EXTM3U");
                foreach (var item in allTracks)
                {
                    sb.AppendLine($"#EXTINF:-1,{item.Entry.Title ?? Path.GetFileNameWithoutExtension(item.Path)}");
                    sb.AppendLine(item.Path);
                }

                File.WriteAllText(savePath, sb.ToString(), System.Text.Encoding.UTF8);
            });

            NotificationService.Instance.Notify(LocalizationService.Instance["TitleListExported"], string.Format(LocalizationService.Instance["MsgListExportedM3u8"], allTracks.Count), NotificationType.Success);
            DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["HistoryMsgExportDone"], savePath), LocalizationService.Instance["TitleExportDone"]);
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowMessage(string.Format(LocalizationService.Instance["HistoryMsgExportFailed"], ex.Message), LocalizationService.Instance["TitleError"], MessageBoxImage.Error);
        }
    }

    public void ExportPlaylistM3U8() => _ = ExportPlaylistM3U8Async();

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
    public async Task CleanMissingEntriesAsync()
    {
        var all = HistoryEntries.ToList();
        var missing = await Task.Run(() =>
        {
            return all.Where(e =>
            {
                var path = ResolveActualFilePath(e);
                return string.IsNullOrEmpty(path) || !File.Exists(path);
            }).ToList();
        });

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
            NotificationService.Instance.Notify(LocalizationService.Instance["TitleLibraryCleaned"], string.Format(LocalizationService.Instance["MsgLibraryCleanedCount"], removed), NotificationType.Success);
            LogService.Instance.Info($"Depuración de biblioteca: {removed} registros removidos.");
        }
    }

    public void CleanMissingEntries() => _ = CleanMissingEntriesAsync();
}
