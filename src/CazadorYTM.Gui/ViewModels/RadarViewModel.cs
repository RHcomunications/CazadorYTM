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

public partial class RadarViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly List<RadarItem> _cachedPlaylists = new();

    public event Action<DownloadEntry>? SendToDownloadRequested;
    public event Action<string, string, string?>? PlayTrackRequested;
    public event Action? RequestOpenCookiesManager;

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

    [ObservableProperty]
    private bool _isViewingPlaylistDetails = false;

    [ObservableProperty]
    private string _currentPlaylistTitle = string.Empty;

    [ObservableProperty]
    private RadarItem? _currentPlaylist;

    [ObservableProperty]
    private bool _hasSessionConnected = false;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<RadarItem> AllItems { get; } = new();
    public ObservableCollection<RadarItem> FilteredItems { get; } = new();

    public RadarViewModel()
    {
        _config = new AppConfig();
        HasSessionConnected = CheckSessionConnected();
    }

    public bool CheckSessionConnected()
    {
        try
        {
            var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
            if (File.Exists(cookiesPath))
            {
                var fi = new FileInfo(cookiesPath);
                if (fi.Length > 0)
                {
                    return true;
                }
            }

            var browser = _config.Get("cookies_browser", "Ninguno");
            if (!string.IsNullOrWhiteSpace(browser) && !browser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedSectionChanged(RadarSection value)
    {
        IsViewingPlaylistDetails = false;
        CurrentPlaylist = null;
        CurrentPlaylistTitle = string.Empty;
        _ = LoadSectionAsync(value);
    }

    [RelayCommand]
    public async Task RefreshCurrentSection()
    {
        if (IsLoading) return;

        HasSessionConnected = CheckSessionConnected();
        if (IsViewingPlaylistDetails && CurrentPlaylist != null)
        {
            await OpenPlaylist(CurrentPlaylist);
        }
        else
        {
            await LoadSectionAsync(SelectedSection);
        }
    }

    public async Task LoadSectionAsync(RadarSection section)
    {
        if (IsLoading) return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        IsViewingPlaylistDetails = false;
        CurrentPlaylist = null;
        CurrentPlaylistTitle = string.Empty;

        // Comprobación defensiva previa de sesión
        HasSessionConnected = CheckSessionConnected();
        if (!HasSessionConnected)
        {
            AllItems.Clear();
            FilteredItems.Clear();
            StatusText = "Sesión de YouTube no conectada. Conecta tus cookies para acceder a tu historial y biblioteca.";
            IsLoading = false;
            return;
        }

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

            if (section == RadarSection.Playlists)
            {
                _cachedPlaylists.Clear();
                _cachedPlaylists.AddRange(items);
            }

            ApplyFilter();
            StatusText = $"{AllItems.Count} elementos cargados en {section}.";
            HasError = false;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = $"Error al consultar Radar: {ex.Message}";
            LogService.Instance.Error($"Error en Radar YTM: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenPlaylist(RadarItem? playlistItem)
    {
        if (IsLoading) return;

        playlistItem ??= SelectedItem;
        if (playlistItem == null || !playlistItem.IsPlaylist) return;

        if (!IsViewingPlaylistDetails)
        {
            _cachedPlaylists.Clear();
            _cachedPlaylists.AddRange(AllItems);
        }

        CurrentPlaylist = playlistItem;
        CurrentPlaylistTitle = playlistItem.Title;
        IsViewingPlaylistDetails = true;
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusText = $"Abriendo y cargando pistas de '{playlistItem.Title}'...";

        try
        {
            var browser = _config.Get("cookies_browser", "Ninguno");
            var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

            var tracks = await RadarService.FetchPlaylistTracksAsync(
                playlistItem.Url ?? playlistItem.Id,
                cookiesBrowser: browser,
                cookiesPath: cookiesPath,
                logger: msg => LogService.Instance.Info(msg));

            AllItems.Clear();
            foreach (var trk in tracks)
            {
                AllItems.Add(trk);
            }

            ApplyFilter();
            StatusText = $"Lista '{playlistItem.Title}' abierta con {AllItems.Count} pistas. Pulsa Enter para reproducir o Flecha Izquierda para volver.";
            HasError = false;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = $"Error al cargar pistas de la lista: {ex.Message}";
            LogService.Instance.Error($"Error al abrir lista en Radar: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void BackToPlaylists()
    {
        if (!IsViewingPlaylistDetails) return;

        IsViewingPlaylistDetails = false;
        var prevId = CurrentPlaylist?.Id;
        CurrentPlaylist = null;
        CurrentPlaylistTitle = string.Empty;
        HasError = false;

        AllItems.Clear();
        foreach (var pl in _cachedPlaylists)
        {
            AllItems.Add(pl);
        }

        ApplyFilter();

        if (!string.IsNullOrEmpty(prevId))
        {
            SelectedItem = FilteredItems.FirstOrDefault(i => i.Id == prevId) ?? FilteredItems.FirstOrDefault();
        }

        StatusText = $"De regreso en Mis Listas de Reproducción ({AllItems.Count} listas disponibles).";
    }

    [RelayCommand]
    public async Task PlayPlaylist(RadarItem? playlistItem)
    {
        playlistItem ??= SelectedItem ?? CurrentPlaylist;
        if (playlistItem == null) return;

        StatusText = $"Iniciando reproducción de la lista '{playlistItem.Title}'...";

        if (IsViewingPlaylistDetails && FilteredItems.Count > 0)
        {
            var firstTrack = FilteredItems.First();
            await PlayItem(firstTrack);
            return;
        }

        var browser = _config.Get("cookies_browser", "Ninguno");
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

        try
        {
            var tracks = await RadarService.FetchPlaylistTracksAsync(
                playlistItem.Url ?? playlistItem.Id,
                cookiesBrowser: browser,
                cookiesPath: cookiesPath,
                logger: msg => LogService.Instance.Info(msg));

            if (tracks.Count > 0)
            {
                var first = tracks[0];
                await PlayItem(first);
                StatusText = $"Reproduciendo primer tema de '{playlistItem.Title}': {first.Title}";
            }
            else
            {
                StatusText = $"No se encontraron canciones en la lista '{playlistItem.Title}'.";
                NotificationService.Instance.NotifyError("Radar YTM", $"No se pudo reproducir la lista: {playlistItem.Title}");
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = $"Error al reproducir lista: {ex.Message}";
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

        if (item.IsPlaylist)
        {
            await PlayPlaylist(item);
            return;
        }

        StatusText = $"Obteniendo enlace de reproducción para: {item.Title}...";
        var browser = _config.Get("cookies_browser", "Ninguno");
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

        try
        {
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
        catch (Exception ex)
        {
            StatusText = $"Error al obtener stream: {ex.Message}";
            NotificationService.Instance.NotifyError("Error de Stream", ex.Message);
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

    [RelayCommand]
    public void OpenCookiesManager()
    {
        RequestOpenCookiesManager?.Invoke();
        // Verificar si la sesión se conectó después de cerrar el diálogo
        if (CheckSessionConnected())
        {
            HasSessionConnected = true;
            _ = LoadSectionAsync(SelectedSection);
        }
    }

    [RelayCommand]
    public void ExportLogs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar Registro de Errores de Cazador YTM",
            Filter = "Archivo de Registro (*.log)|*.log|Archivo de Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            FileName = $"cazador_registro_error_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };

        if (dialog.ShowDialog() == true)
        {
            if (LogService.Instance.ExportLogs(dialog.FileName))
            {
                NotificationService.Instance.Notify("Registro Exportado", $"Registro guardado exitosamente en:\n{dialog.FileName}", NotificationType.Success);
            }
            else
            {
                NotificationService.Instance.NotifyError("Exportar Registro", "No se pudo escribir el archivo de registro en el destino seleccionado.");
            }
        }
    }

    [RelayCommand]
    public void ContactSupport()
    {
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
            NotificationService.Instance.NotifyError("Soporte Técnico", $"No se pudo abrir el enlace de soporte: {ex.Message}");
        }
    }
}
