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
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class SelectableTrackViewModel : ObservableObject
{
    public DownloadEntry Entry { get; }
    public int Index { get; }
    public int Total { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Title => Entry.Title ?? "Sin título";
    public string Uploader => string.IsNullOrWhiteSpace(Entry.Uploader) ? "Artista desconocido" : Entry.Uploader;
    public string DurationFormatted => Entry.Duration.HasValue ? Helpers.FormatDuration(Entry.Duration.Value) : "--:--";
    public string DisplayText => $"[{Index} de {Total}] {Title} [{DurationFormatted}] ({Uploader})";

    public SelectableTrackViewModel(DownloadEntry entry, int index, int total, bool initialSelected = false)
    {
        Entry = entry;
        Index = index;
        Total = total;
        _isSelected = initialSelected;
    }

    public override string ToString() => DisplayText;
}

public partial class ResultSelectionViewModel : ObservableObject, IDisposable
{
    private Process? _previewProcess;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _typeDescription;

    [ObservableProperty]
    private string _selectedCountText = "0 seleccionados";

    [ObservableProperty]
    private SelectableTrackViewModel? _selectedTrack;

    [ObservableProperty]
    private bool _isPreviewing = false;

    [ObservableProperty]
    private string _previewButtonText = "Previsualizar";

    [ObservableProperty]
    private string _previewStatusText = string.Empty;

    public ObservableCollection<SelectableTrackViewModel> Tracks { get; } = new();

    public event Action<bool>? RequestClose;

    public ResultSelectionViewModel(string title, string typeDescription, List<DownloadEntry> entries)
    {
        _title = $"{title} ({entries.Count} canciones)";
        _typeDescription = typeDescription;

        var total = entries.Count;
        for (var i = 0; i < total; i++)
        {
            var item = new SelectableTrackViewModel(entries[i], i + 1, total, initialSelected: false);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectableTrackViewModel.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            Tracks.Add(item);
        }

        if (Tracks.Count > 0)
        {
            SelectedTrack = Tracks[0];
        }

        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        var selected = Tracks.Count(t => t.IsSelected);
        SelectedCountText = $"{selected} de {Tracks.Count} seleccionados";
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var track in Tracks)
        {
            track.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var track in Tracks)
        {
            track.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task PreviewSelected()
    {
        if (IsPreviewing)
        {
            StopPreview();
            return;
        }

        var target = SelectedTrack ?? Tracks.FirstOrDefault(t => t.IsSelected) ?? Tracks.FirstOrDefault();
        if (target == null)
        {
            DialogService.Instance.ShowMessage("Por favor seleccione una pista para previsualizar.", "Aviso", MessageBoxImage.Warning);
            return;
        }

        var query = target.Entry.Url ?? target.Entry.Title ?? "";
        if (string.IsNullOrWhiteSpace(query)) return;

        IsPreviewing = true;
        PreviewButtonText = "Detener Previsualización";
        PreviewStatusText = $"Obteniendo audio para '{target.Title}'...";

        try
        {
            var streamUrl = await Task.Run(() => DownloadEngine.ExtractStreamUrl(query));
            if (string.IsNullOrEmpty(streamUrl))
            {
                PreviewStatusText = "No se pudo obtener el audio de previsualización.";
                DialogService.Instance.ShowMessage("No se pudo obtener el flujo de audio para previsualizar.", "Error", MessageBoxImage.Error);
                StopPreview();
                return;
            }

            var ffplayExe = BinaryManager.GetFfplayPath();
            if (string.IsNullOrEmpty(ffplayExe) || !File.Exists(ffplayExe))
            {
                AudioPlayerService.Instance.Play(streamUrl);
                PreviewStatusText = $"Reproduciendo: {target.Title}";
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ffplayExe,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-nodisp");
            psi.ArgumentList.Add("-autoexit");
            psi.ArgumentList.Add(streamUrl);

            _previewProcess = Process.Start(psi);
            if (_previewProcess != null)
            {
                PreviewStatusText = $"Reproduciendo: {target.Title}";
                _ = Task.Run(() =>
                {
                    try { _previewProcess?.WaitForExit(); }
                    catch { }
                    finally
                    {
                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            IsPreviewing = false;
                            PreviewButtonText = "Previsualizar";
                            PreviewStatusText = string.Empty;
                        });
                    }
                });
            }
        }
        catch (Exception ex)
        {
            PreviewStatusText = $"Error: {ex.Message}";
            StopPreview();
        }
    }

    public void StopPreview()
    {
        try
        {
            if (_previewProcess != null && !_previewProcess.HasExited)
            {
                DownloadEngine.KillProcessTree(_previewProcess.Id);
            }
        }
        catch { }
        finally
        {
            _previewProcess = null;
            IsPreviewing = false;
            PreviewButtonText = "Previsualizar";
            PreviewStatusText = string.Empty;
            AudioPlayerService.Instance.Stop();
        }
    }

    [RelayCommand]
    private void ExportTxt()
    {
        var selected = Tracks.Where(t => t.IsSelected).Select(t => t.Entry.Url ?? t.Entry.Title ?? "").ToList();
        if (selected.Count == 0)
        {
            DialogService.Instance.ShowMessage("No hay pistas seleccionadas para exportar.", "Aviso", MessageBoxImage.Warning);
            return;
        }

        var path = DialogService.Instance.SaveFile("pistas_seleccionadas.txt", "Archivos de texto (*.txt)|*.txt");
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                File.WriteAllLines(path, selected);
                DialogService.Instance.ShowMessage($"{selected.Count} pistas exportadas correctamente.", "Éxito", MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DialogService.Instance.ShowMessage($"Error al exportar: {ex.Message}", "Error", MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        StopPreview();
        var selectedCount = Tracks.Count(t => t.IsSelected);
        if (selectedCount == 0)
        {
            DialogService.Instance.ShowMessage("Por favor seleccione al menos una pista para descargar.", "Aviso", MessageBoxImage.Warning);
            return;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        StopPreview();
        RequestClose?.Invoke(false);
    }

    public void Dispose()
    {
        StopPreview();
    }

    public List<DownloadEntry> GetSelectedEntries()
    {
        return Tracks.Where(t => t.IsSelected).Select(t => t.Entry).ToList();
    }
}
