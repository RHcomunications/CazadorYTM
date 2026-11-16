namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly HistoryManager _historyManager;
    private readonly CancellationTokenSource _lifecycleCts = new();
    private DispatcherTimer? _searchDebounceTimer;
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
        if (_searchDebounceTimer != null)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Tick -= OnSearchDebounceTick;
            _searchDebounceTimer = null;
        }
        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();
        HistoryManager.GlobalHistoryChanged -= OnGlobalHistoryChanged;
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        if (!_disposed)
        {
            ApplyFilter();
        }
    }

    private void FlushPendingFilter()
    {
        if (_searchDebounceTimer != null && _searchDebounceTimer.IsEnabled)
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
        }
    }

    partial void OnSearchFilterChanged(string value)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            ApplyFilter();
            return;
        }

        if (_searchDebounceTimer == null)
        {
            _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _searchDebounceTimer.Tick += OnSearchDebounceTick;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    [RelayCommand]
    public void RefreshHistory()
    {
        _searchDebounceTimer?.Stop();
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

    private static List<string?> ResolveActualFilePathsBatch(IReadOnlyList<DownloadEntry> targets, string configuredDestFolder, CancellationToken ct = default)
    {
        var results = new List<string?>(targets.Count);
        var defaultFolders = new List<string>(2);

        var destFolder = configuredDestFolder;
        if (string.IsNullOrEmpty(destFolder) || !Directory.Exists(destFolder))
        {
            destFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Constants.MultiDownloadFolder);
        }
        if (Directory.Exists(destFolder))
        {
            defaultFolders.Add(destFolder);
        }

        // Cache per-folder enumeration once per batch while keeping each entry's fallback scoped to its own folders.
        var folderFileCache = new Dictionary<string, List<(string FullPath, string FileName)>>(StringComparer.OrdinalIgnoreCase);
        List<(string FullPath, string FileName)> GetFolderFiles(string folder)
        {
            if (folderFileCache.TryGetValue(folder, out var cached)) return cached;
            var list = new List<(string, string)>();
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SafeSearchOptions))
                {
                    ct.ThrowIfCancellationRequested();
                    list.Add((file, Path.GetFileName(file)));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            folderFileCache[folder] = list;
            return list;
        }

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(target.FilePath) && File.Exists(target.FilePath))
            {
                results.Add(target.FilePath);
                continue;
            }

            var entryFolders = new List<string>(3);
            if (!string.IsNullOrEmpty(target.FilePath) && Directory.Exists(target.FilePath))
            {
                entryFolders.Add(target.FilePath);
            }
            foreach (var df in defaultFolders)
            {
                if (!entryFolders.Contains(df, StringComparer.OrdinalIgnoreCase))
                {
                    entryFolders.Add(df);
                }
            }

            string? resolved = null;
            if (!string.IsNullOrEmpty(target.FilePath) && !Directory.Exists(target.FilePath))
            {
                var fname = Path.GetFileName(target.FilePath);
                if (!string.IsNullOrEmpty(fname))
                {
                    foreach (var folder in entryFolders)
                    {
                        var test = Path.Combine(folder, fname);
                        if (File.Exists(test))
                        {
                            resolved = test;
                            break;
                        }
                    }
                }
            }

            if (resolved == null && !string.IsNullOrWhiteSpace(target.Title))
            {
                var sanitizedTitle = Helpers.SanitizeFilename(target.Title);
                if (!string.IsNullOrWhiteSpace(sanitizedTitle))
                {
                    foreach (var folder in entryFolders)
                    {
                        var match = GetFolderFiles(folder)
                            .FirstOrDefault(f => f.FileName.Contains(sanitizedTitle, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match.FullPath))
                        {
                            resolved = match.FullPath;
                            break;
                        }
                    }
                }
            }

            results.Add(resolved ?? target.FilePath);
        }

        return results;
    }

    private string? ResolveActualFilePath(DownloadEntry target)
    {
        var dest = new AppConfig().Get("destination_folder", "");
        return ResolveActualFilePathsBatch(new[] { target }, dest)[0];
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
        FlushPendingFilter();
        var snapshot = FilteredEntries.ToList();
        var destFolder = new AppConfig().Get("destination_folder", "");
        var ct = _disposed ? new CancellationToken(true) : _lifecycleCts.Token;
        List<string> allTracks;
        try
        {
            allTracks = await Task.Run(() =>
            {
                return ResolveActualFilePathsBatch(snapshot, destFolder, ct)
                    .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                    .Cast<string>()
                    .ToList();
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_disposed || ct.IsCancellationRequested) return;

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
        FlushPendingFilter();
        var snapshot = FilteredEntries.ToList();
        if (snapshot.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNoAudioToPlay"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var destFolder = new AppConfig().Get("destination_folder", "");
        var ct = _disposed ? new CancellationToken(true) : _lifecycleCts.Token;
        List<string> allTracks;
        try
        {
            allTracks = await Task.Run(() =>
            {
                return ResolveActualFilePathsBatch(snapshot, destFolder, ct)
                    .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                    .Cast<string>()
                    .ToList();
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_disposed || ct.IsCancellationRequested) return;

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
        FlushPendingFilter();
        var snapshot = FilteredEntries.ToList();
        if (snapshot.Count == 0)
        {
            DialogService.Instance.ShowMessage(LocalizationService.Instance["HistoryMsgNothingToExport"], LocalizationService.Instance["TitleWarning"], MessageBoxImage.Information);
            return;
        }

        var destFolder = new AppConfig().Get("destination_folder", "");
        var ct = _disposed ? new CancellationToken(true) : _lifecycleCts.Token;
        List<(DownloadEntry Entry, string? Path)> allTracks;
        try
        {
            allTracks = await Task.Run(() =>
            {
                var resolved = ResolveActualFilePathsBatch(snapshot, destFolder, ct);
                return snapshot
                    .Zip(resolved, (e, p) => (Entry: e, Path: p))
                    .Where(t => !string.IsNullOrEmpty(t.Path) && File.Exists(t.Path))
                    .ToList();
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_disposed || ct.IsCancellationRequested) return;

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
    public async Task OpenContainingFolderAsync(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        var destFolder = new AppConfig().Get("destination_folder", "");
        if (target == null)
        {
            if (Directory.Exists(destFolder))
            {
                Process.Start("explorer.exe", destFolder);
            }
            return;
        }

        var ct = _disposed ? new CancellationToken(true) : _lifecycleCts.Token;
        string? realPath;
        try
        {
            realPath = await Task.Run(() => ResolveActualFilePathsBatch(new[] { target }, destFolder, ct)[0], ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (_disposed || ct.IsCancellationRequested) return;

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
        var destFolder = new AppConfig().Get("destination_folder", "");
        var ct = _disposed ? new CancellationToken(true) : _lifecycleCts.Token;
        List<DownloadEntry> missing;
        try
        {
            missing = await Task.Run(() =>
            {
                var resolved = ResolveActualFilePathsBatch(all, destFolder, ct);
                return all
                    .Zip(resolved, (e, p) => (Entry: e, Path: p))
                    .Where(t => string.IsNullOrEmpty(t.Path) || !File.Exists(t.Path))
                    .Select(t => t.Entry)
                    .ToList();
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_disposed || ct.IsCancellationRequested) return;

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
