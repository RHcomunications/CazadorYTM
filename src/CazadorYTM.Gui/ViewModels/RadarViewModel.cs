namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class RadarViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public event Action<DownloadEntry>? SendToDownloadRequested;
    public event Action<string, string, string?>? PlayTrackRequested;

    [ObservableProperty]
    private RadarSection _selectedSection = RadarSection.History;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusText = "Listo para explorar Radar YTM";

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private RadarItem? _selectedItem;

    public ObservableCollection<RadarItem> AllItems { get; } = new();
    public ObservableCollection<RadarItem> FilteredItems { get; } = new();

    public RadarViewModel()
    {
        _config = new AppConfig();
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedSectionChanged(RadarSection value)
    {
        _ = LoadSectionAsync(value);
    }

    [RelayCommand]
    public async Task RefreshCurrentSection()
    {
        await LoadSectionAsync(SelectedSection);
    }

    public async Task LoadSectionAsync(RadarSection section)
    {
        IsLoading = true;
        StatusText = section switch
        {
            RadarSection.History => "Consultando historial reciente de YouTube...",
            RadarSection.LikedSongs => "Consultando canciones que te gustan (Liked Music)...",
            RadarSection.Playlists => "Consultando tus listas de reproducción...",
            _ => "Cargando..."
        };

        try
        {
            var browser = _config.Get("cookies_browser", "Ninguno");
            var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

            var items = await RadarService.FetchRadarItemsAsync(
                section,
                cookiesBrowser: browser,
                cookiesPath: cookiesPath,
                logger: msg => LogService.Instance.Info(msg));

            AllItems.Clear();
            foreach (var it in items)
            {
                AllItems.Add(it);
            }

            ApplyFilter();
            StatusText = $"{AllItems.Count} elementos cargados en {section}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al consultar Radar: {ex.Message}";
            LogService.Instance.Error($"Error en Radar YTM: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var q = FilterQuery.Trim().ToLower();

        var queryList = string.IsNullOrWhiteSpace(q)
            ? AllItems
            : AllItems.Where(i => i.Title.ToLower().Contains(q) || (i.Artist ?? "").ToLower().Contains(q));

        foreach (var it in queryList)
        {
            FilteredItems.Add(it);
        }

        if (SelectedItem == null || !FilteredItems.Contains(SelectedItem))
        {
            SelectedItem = FilteredItems.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task PlayItem(RadarItem? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        StatusText = $"Obteniendo enlace de reproducción para: {item.Title}...";
        var browser = _config.Get("cookies_browser", "Ninguno");
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

        var streamUrl = await RadarService.GetStreamUrlAsync(item.Url ?? item.Id, browser, cookiesPath);
        if (!string.IsNullOrEmpty(streamUrl))
        {
            PlayTrackRequested?.Invoke(streamUrl, item.Title, item.Artist);
            StatusText = $"Reproduciendo: {item.Title}";
        }
        else
        {
            StatusText = $"No se pudo obtener el stream para {item.Title}.";
            NotificationService.Instance.NotifyError("Radar YTM", $"No se pudo reproducir en directo: {item.Title}");
        }
    }

    [RelayCommand]
    public void SendToDownload(RadarItem? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        var entry = item.ToDownloadEntry();
        SendToDownloadRequested?.Invoke(entry);
        NotificationService.Instance.Notify("Radar YTM", $"Enviado a Descargas: {item.Title}", NotificationType.Info);
    }
}
