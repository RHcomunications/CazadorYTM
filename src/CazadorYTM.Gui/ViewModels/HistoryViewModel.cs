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
using CazadorYTM.Gui.Services;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryManager _historyManager;

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
            DialogService.Instance.ShowMessage("Seleccione una canción del historial para reproducir.", "Aviso", MessageBoxImage.Information);
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
            DialogService.Instance.ShowMessage($"El archivo '{target.Title}' no se encuentra en el disco:\n{target.FilePath}", "Aviso", MessageBoxImage.Warning);
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
            DialogService.Instance.ShowMessage("No hay archivos de audio disponibles en la biblioteca para reproducir.", "Aviso", MessageBoxImage.Information);
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
            DialogService.Instance.ShowMessage("No hay archivos en la biblioteca para exportar a lista de reproducción.", "Aviso", MessageBoxImage.Information);
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
            DialogService.Instance.ShowMessage($"Lista de reproducción guardada exitosamente:\n{savePath}", "Exportación Completada");
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowMessage($"Error al exportar la lista de reproducción: {ex.Message}", "Error", MessageBoxImage.Error);
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
                DialogService.Instance.ShowMessage($"No se pudo abrir la carpeta: {ex.Message}", "Error", MessageBoxImage.Error);
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
                DialogService.Instance.ShowMessage("No se encontró la carpeta de descargas.", "Aviso", MessageBoxImage.Information);
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
            DialogService.Instance.ShowMessage("Enlace copiado al portapapeles.", "Información", MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    public void DeleteSelected(DownloadEntry? entry = null)
    {
        var target = entry ?? SelectedEntry ?? FilteredEntries.FirstOrDefault();
        if (target != null)
        {
            if (DialogService.Instance.ShowConfirmation($"¿Desea eliminar '{target.Title}' del historial?", "Confirmar eliminación"))
            {
                _historyManager.RemoveEntry(target);
                RefreshHistory();
            }
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        if (DialogService.Instance.ShowConfirmation("¿Está seguro de que desea vaciar todo el historial de descargas?", "Vaciar historial"))
        {
            _historyManager.Clear();
            RefreshHistory();
            LogService.Instance.Info("Historial de descargas vaciado.");
        }
    }
}
