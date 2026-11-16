namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class RadarViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _config;
    private readonly List<RadarItem> _cachedPlaylists = new();
    private readonly Dictionary<RadarSection, List<RadarItem>> _sectionCache = new();
    private readonly Dictionary<RadarSection, DateTime> _sectionCacheAt = new();
    private static readonly TimeSpan SectionCacheTtl = TimeSpan.FromMinutes(5);
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _streamCts;
    private bool _hasLoadedOnce;
    private bool _disposed;

    public event Action<DownloadEntry>? SendToDownloadRequested;
    public event Action<string, string, string?>? PlayTrackRequested;
    public event Action? RequestOpenCookiesManager;
    public event Action<RadarItem?>? RequestFocusItem;

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
    private bool _isSessionExpired = false;

    [ObservableProperty]
    private bool _isEmpty = false;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<RadarItem> AllItems { get; } = new();
    public ObservableCollection<RadarItem> FilteredItems { get; } = new();

    private readonly CancellationTokenSource _lifecycleCts = new();
    private readonly Func<RadarSection, CancellationToken, Task<List<RadarItem>>>? _fetchSectionOverride;
    private readonly Func<bool>? _sessionCheckOverride;

    public RadarViewModel()
        : this(null, null, null)
    {
    }

    public RadarViewModel(
        AppConfig? config,
        Func<RadarSection, CancellationToken, Task<List<RadarItem>>>? fetchSectionAsync = null,
        Func<bool>? checkSessionConnected = null)
    {
        _config = config ?? new AppConfig();
        _fetchSectionOverride = fetchSectionAsync;
        _sessionCheckOverride = checkSessionConnected;
        _statusText = LocalizationService.Instance["RadarStatusReady"];
        HasSessionConnected = CheckSessionConnected();
    }

    public bool CheckSessionConnected()
    {
        if (_sessionCheckOverride != null)
        {
            return _sessionCheckOverride();
        }
        try
        {
            _config.Load();
            var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
            if (File.Exists(cookiesPath))
            {
                return YouTubeMusicSession.TryCreate(cookiesPath) != null;
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

    partial void OnSelectedItemChanged(RadarItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedTrack));
        LikeSelectedCommand.NotifyCanExecuteChanged();
        DislikeSelectedCommand.NotifyCanExecuteChanged();
        ClearRatingCommand.NotifyCanExecuteChanged();
        AddToPlaylistCommand.NotifyCanExecuteChanged();
    }

    public bool HasSelectedTrack => SelectedItem != null && !SelectedItem.IsPlaylist;

    private bool CanRateSelected() => HasSelectedTrack;

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
        IsSessionExpired = false;
        HasSessionConnected = CheckSessionConnected();
        if (IsViewingPlaylistDetails && CurrentPlaylist != null)
        {
            await OpenPlaylist(CurrentPlaylist);
        }
        else
        {
            await LoadSectionAsync(SelectedSection, force: true);
        }
    }

    public async Task LoadSectionAsync(RadarSection section, bool force = false)
    {
        var cts = BeginLoad(out var token);

        IsLoading = true;
        HasError = false;
        IsEmpty = false;
        IsSessionExpired = false;
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
            StatusText = LocalizationService.Instance["RadarStatusNoSession"];
            return;
        }

        if (!force && TryUseCachedSection(section))
        {
            return;
        }

        StatusText = section switch
        {
            RadarSection.History => LocalizationService.Instance["RadarStatusLoadingHistory"],
            RadarSection.LikedSongs => LocalizationService.Instance["RadarStatusLoadingLiked"],
            RadarSection.Playlists => LocalizationService.Instance["RadarStatusLoadingPlaylists"],
            RadarSection.Albums => LocalizationService.Instance.Get("RadarStatusLoadingAlbums", "Cargando álbumes guardados..."),
            RadarSection.Artists => LocalizationService.Instance.Get("RadarStatusLoadingArtists", "Cargando artistas seguidos..."),
            _ => "..."
        };

        try
        {
            var browser = _config.Get("cookies_browser", "Ninguno");
            var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

            var items = _fetchSectionOverride != null
                ? await _fetchSectionOverride(section, token)
                : await RadarService.FetchRadarItemsAsync(
                    section,
                    cookiesBrowser: browser,
                    cookiesPath: cookiesPath,
                    logger: msg => LogService.Instance.Info(msg),
                    language: _config.Get("language", "es"),
                    cancellationToken: token);

            if (_disposed || token.IsCancellationRequested)
            {
                return;
            }

            _sectionCache[section] = items;
            _sectionCacheAt[section] = DateTime.UtcNow;

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

            _hasLoadedOnce = true;
            ApplyFilter();
            IsSessionExpired = false;
            HasError = false;
            StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusItemsLoaded", "{0} elementos cargados en {1}."), AllItems.Count, section);
            RequestFocusItem?.Invoke(SelectedItem);
        }
        catch (OperationCanceledException)
        {
        }
        catch (YouTubeMusicSessionExpiredException ex)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            AllItems.Clear();
            FilteredItems.Clear();
            HasSessionConnected = false;
            IsSessionExpired = true;
            HasError = true;
            IsEmpty = false;
            ErrorMessage = LocalizationService.Instance["RadarSessionExpiredDesc"];
            StatusText = LocalizationService.Instance["RadarStatusSessionExpired"];
            LogService.Instance.Warn($"Sesión de YouTube Music caducada: {ex.Message}");
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusErrorQuery", "Error al consultar Radar: {0}"), ex.Message);
            LogService.Instance.Error($"Error en Radar YTM: {ex.Message}");
        }
        finally
        {
            CompleteLoad(cts);
        }
    }

    private CancellationTokenSource BeginLoad(out CancellationToken token)
    {
        var cts = new CancellationTokenSource();
        var previous = _loadCts;
        _loadCts = cts;
        previous?.Cancel();
        token = cts.Token;
        return cts;
    }

    private void CompleteLoad(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_loadCts, cts))
        {
            _loadCts = null;
            IsLoading = false;
        }

        cts.Dispose();
    }

    private bool TryUseCachedSection(RadarSection section)
    {
        if (!_sectionCache.TryGetValue(section, out var items) ||
            !_sectionCacheAt.TryGetValue(section, out var loadedAt) ||
            DateTime.UtcNow - loadedAt > SectionCacheTtl)
        {
            return false;
        }

        AllItems.Clear();
        foreach (var item in items)
        {
            AllItems.Add(item);
        }

        ApplyFilter();
        StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusItemsLoadedCache", "{0} elementos cargados en {1} (caché)."), AllItems.Count, section);
        HasError = false;
        RequestFocusItem?.Invoke(SelectedItem);
        return true;
    }

    [RelayCommand]
    public async Task OpenPlaylist(RadarItem? playlistItem)
    {
        playlistItem ??= SelectedItem;
        if (playlistItem == null || !playlistItem.IsPlaylist) return;

        var cts = BeginLoad(out var token);

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
        StatusText = string.Format(LocalizationService.Instance["RadarStatusOpeningPlaylist"], playlistItem.Title);

        try
        {
            var browser = _config.Get("cookies_browser", "Ninguno");
            var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

            var tracks = await RadarService.FetchPlaylistTracksAsync(
                playlistItem.Url ?? playlistItem.Id,
                cookiesBrowser: browser,
                cookiesPath: cookiesPath,
                logger: msg => LogService.Instance.Info(msg),
                language: _config.Get("language", "es"),
                cancellationToken: token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            AllItems.Clear();
            foreach (var trk in tracks)
            {
                AllItems.Add(trk);
            }

            _hasLoadedOnce = true;
            ApplyFilter();
            SelectedItem = FilteredItems.FirstOrDefault();
            StatusText = string.Format(LocalizationService.Instance["RadarStatusPlaylistOpened"], playlistItem.Title, AllItems.Count);
            HasError = false;
            RequestFocusItem?.Invoke(SelectedItem);
        }
        catch (OperationCanceledException)
        {
        }
        catch (YouTubeMusicSessionExpiredException ex)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            AllItems.Clear();
            FilteredItems.Clear();
            HasSessionConnected = false;
            IsSessionExpired = true;
            HasError = true;
            IsEmpty = false;
            ErrorMessage = LocalizationService.Instance["RadarSessionExpiredDesc"];
            StatusText = LocalizationService.Instance["RadarStatusSessionExpired"];
            LogService.Instance.Warn($"Sesión caducada al abrir lista: {ex.Message}");
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusErrorPlaylistTracks", "Error al cargar pistas de la lista: {0}"), ex.Message);
            LogService.Instance.Error($"Error al abrir lista en Radar: {ex.Message}");
        }
        finally
        {
            CompleteLoad(cts);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRateSelected))]
    public async Task LikeSelected() => await SetRatingAsync(SelectedItem, RadarRating.Like);

    [RelayCommand(CanExecute = nameof(CanRateSelected))]
    public async Task DislikeSelected() => await SetRatingAsync(SelectedItem, RadarRating.Dislike);

    [RelayCommand(CanExecute = nameof(CanRateSelected))]
    public async Task ClearRating() => await SetRatingAsync(SelectedItem, RadarRating.None);

    private async Task SetRatingAsync(RadarItem? item, RadarRating rating)
    {
        if (_disposed || item == null || item.IsPlaylist)
        {
            return;
        }

        var ct = _lifecycleCts.Token;
        var loc = LocalizationService.Instance;
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        var language = _config.Get("language", "es");
        StatusText = loc["RadarStatusSavingRating"];

        try
        {
            await RadarService.SetTrackRatingAsync(item.Id, rating, cookiesPath, language, ct);
            if (_disposed || ct.IsCancellationRequested) return;

            StatusText = rating switch
            {
                RadarRating.Like => Format(loc["RadarStatusLikeSaved"], item.Title),
                RadarRating.Dislike => Format(loc["RadarStatusDislikeSaved"], item.Title),
                _ => Format(loc["RadarStatusRatingRemoved"], item.Title)
            };
            HasError = false;
            NotificationService.Instance.Notify("Radar YTM", StatusText, NotificationType.Success);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = Format(loc["RadarErrorRating"], ex.Message);
            NotificationService.Instance.NotifyError("Radar YTM", StatusText);
            LogService.Instance.Error($"Error al valorar pista en Radar: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRateSelected))]
    public async Task AddToPlaylist()
    {
        var item = SelectedItem;
        if (_disposed || item == null || item.IsPlaylist)
        {
            return;
        }

        var ct = _lifecycleCts.Token;
        var loc = LocalizationService.Instance;
        var previousStatus = StatusText;
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        var language = _config.Get("language", "es");
        StatusText = loc["RadarStatusLoadingPlaylists"];

        try
        {
            if (_cachedPlaylists.Count == 0)
            {
                var playlists = await RadarService.FetchRadarItemsAsync(
                    RadarSection.Playlists,
                    cookiesBrowser: _config.Get("cookies_browser", "Ninguno"),
                    cookiesPath: cookiesPath,
                    logger: msg => LogService.Instance.Info(msg),
                    language: language,
                    cancellationToken: ct);

                if (_disposed || ct.IsCancellationRequested) return;
                _cachedPlaylists.Clear();
                _cachedPlaylists.AddRange(playlists.Where(p => p.IsPlaylist));
            }

            if (_disposed || ct.IsCancellationRequested) return;
            var choices = _cachedPlaylists
                .Where(p => p.IsPlaylist && p.Id != "VLLM" && p.Id != "LM")
                .ToList();

            var selection = DialogService.Instance.ShowPlaylistPicker(item.Title, choices);
            if (selection == null)
            {
                StatusText = previousStatus;
                return;
            }

            if (!string.IsNullOrWhiteSpace(selection.NewPlaylistTitle))
            {
                await RadarService.CreatePlaylistWithTrackAsync(
                    selection.NewPlaylistTitle!, item.Id, cookiesPath, language, ct);

                if (_disposed || ct.IsCancellationRequested) return;
                InvalidatePlaylistCaches();
                StatusText = Format(loc["RadarStatusPlaylistCreated"], selection.NewPlaylistTitle!, item.Title);
            }
            else if (!string.IsNullOrWhiteSpace(selection.PlaylistId))
            {
                await RadarService.AddTrackToPlaylistAsync(
                    selection.PlaylistId!, item.Id, cookiesPath, language, ct);

                if (_disposed || ct.IsCancellationRequested) return;

                var playlistTitle = _cachedPlaylists
                    .FirstOrDefault(p => p.Id == selection.PlaylistId)?.Title ?? selection.PlaylistId!;
                StatusText = Format(loc["RadarStatusAddedToPlaylist"], item.Title, playlistTitle);
            }

            HasError = false;
            NotificationService.Instance.Notify("Radar YTM", StatusText, NotificationType.Success);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = Format(loc["RadarErrorAddToPlaylist"], ex.Message);
            NotificationService.Instance.NotifyError("Radar YTM", StatusText);
            LogService.Instance.Error($"Error al añadir a lista en Radar: {ex.Message}");
        }
    }

    private void InvalidatePlaylistCaches()
    {
        _sectionCache.Remove(RadarSection.Playlists);
        _sectionCacheAt.Remove(RadarSection.Playlists);
    }

    private static string Format(string template, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            template = template.Replace("{" + i + "}", values[i]);
        }

        return template;
    }

    [RelayCommand]
    public void BackToPlaylists()
    {
        if (!IsViewingPlaylistDetails) return;

        _loadCts?.Cancel();
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

        StatusText = string.Format(LocalizationService.Instance["RadarStatusBackToPlaylists"], AllItems.Count);
        RequestFocusItem?.Invoke(SelectedItem);
    }

    [RelayCommand]
    public async Task PlayPlaylist(RadarItem? playlistItem)
    {
        playlistItem ??= SelectedItem ?? CurrentPlaylist;
        if (playlistItem == null) return;

        StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusStartingPlaylist", "Iniciando reproducción de la lista '{0}'..."), playlistItem.Title);

        if (IsViewingPlaylistDetails && FilteredItems.Count > 0)
        {
            var firstTrack = FilteredItems.FirstOrDefault(i => !i.IsPlaylist);
            if (firstTrack == null)
            {
                StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusNoTracksInPlaylist", "No se encontraron canciones en la lista '{0}'."), playlistItem.Title);
                return;
            }
            await PlayItem(firstTrack);
            return;
        }

        var browser = _config.Get("cookies_browser", "Ninguno");
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        if (_disposed) return;
        var ct = _lifecycleCts.Token;

        try
        {
            var tracks = await RadarService.FetchPlaylistTracksAsync(
                playlistItem.Url ?? playlistItem.Id,
                cookiesBrowser: browser,
                cookiesPath: cookiesPath,
                logger: msg => LogService.Instance.Info(msg),
                language: _config.Get("language", "es"),
                cancellationToken: ct);

            if (_disposed || ct.IsCancellationRequested) return;

            if (tracks.Count > 0)
            {
                var first = tracks[0];
                await PlayItem(first);
                if (_disposed || ct.IsCancellationRequested) return;
                StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusPlayingFirstTrack", "Reproduciendo primer tema de '{0}': {1}"), playlistItem.Title, first.Title);
            }
            else
            {
                StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusNoTracksInPlaylist", "No se encontraron canciones en la lista '{0}'."), playlistItem.Title);
                NotificationService.Instance.NotifyError("Radar YTM", StatusText);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
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
        var q = FilterQuery.Trim();

        var queryList = string.IsNullOrWhiteSpace(q)
            ? AllItems
            : AllItems.Where(i => (i.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                  (i.Artist ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var it in queryList)
        {
            FilteredItems.Add(it);
        }

        if (SelectedItem == null || !FilteredItems.Contains(SelectedItem))
        {
            SelectedItem = FilteredItems.FirstOrDefault();
        }

        UpdateIsEmpty();
    }

    private void UpdateIsEmpty()
    {
        if (!_hasLoadedOnce && AllItems.Count == 0)
        {
            IsEmpty = false;
            return;
        }

        IsEmpty = HasSessionConnected && !HasError && !IsLoading && FilteredItems.Count == 0;
    }

    partial void OnIsLoadingChanged(bool value) => UpdateIsEmpty();
    partial void OnHasErrorChanged(bool value) => UpdateIsEmpty();
    partial void OnHasSessionConnectedChanged(bool value) => UpdateIsEmpty();

    [RelayCommand]
    public async Task PlayItem(RadarItem? item)
    {
        item ??= SelectedItem;
        if (_disposed || item == null) return;

        if (item.IsPlaylist)
        {
            await PlayPlaylist(item);
            return;
        }

        StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusGettingStreamUrl", "Obteniendo enlace de reproducción para: {0}..."), item.Title);
        var browser = _config.Get("cookies_browser", "Ninguno");
        var cookiesPath = _config.Get("cookies_path", Helpers.GetDefaultCookiesPath());

        var cts = new CancellationTokenSource();
        var prevCts = _streamCts;
        _streamCts = cts;
        prevCts?.Cancel();
        prevCts?.Dispose();

        try
        {
            var streamUrl = await RadarService.GetStreamUrlAsync(item.Url ?? item.Id, browser, cookiesPath, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(streamUrl))
            {
                PlayTrackRequested?.Invoke(streamUrl, item.Title, item.Artist);
                StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusPlayingTrack", "Reproduciendo: {0}"), item.Title);
            }
            else
            {
                StatusText = string.Format(LocalizationService.Instance.Get("RadarStatusCannotGetStream", "No se pudo obtener el stream para {0}."), item.Title);
                NotificationService.Instance.NotifyError("Radar YTM", StatusText);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (cts.Token.IsCancellationRequested) return;
            StatusText = $"Error al obtener stream: {ex.Message}";
            NotificationService.Instance.NotifyError(LocalizationService.Instance.Get("TitleStreamError", "Error de Stream"), ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_streamCts, cts))
            {
                _streamCts = null;
            }
            cts.Dispose();
        }
    }

    [RelayCommand]
    public void SendToDownload(RadarItem? item)
    {
        item ??= SelectedItem;
        if (_disposed || item == null) return;

        var entry = item.ToDownloadEntry();
        SendToDownloadRequested?.Invoke(entry);
        NotificationService.Instance.Notify("Radar YTM", string.Format(LocalizationService.Instance.Get("RadarStatusSentToDownloads", "Enviado a Descargas: {0}"), item.Title), NotificationType.Info);
    }

    [RelayCommand]
    public void OpenCookiesManager()
    {
        RequestOpenCookiesManager?.Invoke();
        // Verificar si la sesión se conectó después de cerrar el diálogo
        if (CheckSessionConnected())
        {
            HasSessionConnected = true;
            _sectionCache.Clear();
            _sectionCacheAt.Clear();
            _ = LoadSectionAsync(SelectedSection, force: true);
        }
    }

    [RelayCommand]
    public void ExportLogs() => AppActionsHelper.ExportLogs("cazador_registro_error");

    [RelayCommand]
    public void ContactSupport() => AppActionsHelper.ContactSupport();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
    }
}
