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
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class DownloadViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly HistoryManager _historyManager;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _selectedFormat = "mp3";

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
    private string _destinationFolder = string.Empty;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private double _progressPercentage = 0;

    [ObservableProperty]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    private string _statusText = "Listo para descargar";

    [ObservableProperty]
    private string _selectedSearchSource = "YouTube Music";

    [ObservableProperty]
    private bool _isClipboardMonitorEnabled = false;

    [ObservableProperty]
    private bool _hasActiveBatchSlots = false;

    public ObservableCollection<string> Formats { get; } = new(Constants.FormatsDisplay);
    public ObservableCollection<string> Bitrates { get; } = new(Constants.Bitrates);
    public ObservableCollection<int> ConcurrencyOptions { get; } = new(Constants.MaxConcurrentValues);
    public ObservableCollection<string> Browsers { get; } = new(Constants.Browsers);
    public ObservableCollection<string> SearchSources { get; } = new(Constants.SearchSources);
    public ObservableCollection<ActiveDownloadSlot> ActiveSlots { get; } = new();

    private readonly System.Windows.Threading.DispatcherTimer _clipboardTimer;
    private string _lastClipboardUrl = string.Empty;

    public DownloadViewModel()
    {
        _config = new AppConfig();
        _historyManager = new HistoryManager();

        _selectedFormat = _config.Get("format", "mp3");
        _selectedBitrate = _config.Get("bitrate", "320K");
        _selectedConcurrent = _config.Get("max_concurrent", 3);
        _selectedBrowser = _config.Get("cookies_browser", "Ninguno");
        _isSponsorBlock = _config.Get("sponsorblock", true);
        _isNormalize = _config.Get("normalize", true);
        _isEmbedLyrics = _config.Get("embed_lyrics", false);
        _isEnumerate = _config.Get("enumerar", false);
        _isClipboardMonitorEnabled = _config.Get("clipboard_monitor", false);

        var savedDest = _config.Get("destination_folder", "");
        _destinationFolder = string.IsNullOrWhiteSpace(savedDest) || !Directory.Exists(savedDest)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Constants.MultiDownloadFolder)
            : savedDest;

        Directory.CreateDirectory(_destinationFolder);

        _clipboardTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _clipboardTimer.Tick += OnClipboardTimerTick;
        if (_isClipboardMonitorEnabled)
        {
            _clipboardTimer.Start();
        }
    }

    partial void OnIsClipboardMonitorEnabledChanged(bool value)
    {
        if (value)
            _clipboardTimer.Start();
        else
            _clipboardTimer.Stop();

        SavePreferences();
    }

    private void OnClipboardTimerTick(object? sender, EventArgs e)
    {
        if (!IsClipboardMonitorEnabled || IsBusy) return;

        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrEmpty(text) && text != _lastClipboardUrl && Helpers.IsUrl(text) &&
                    (text.Contains("youtube.com/") || text.Contains("youtu.be/") || text.Contains("soundcloud.com/")))
                {
                    _lastClipboardUrl = text;
                    if (string.IsNullOrWhiteSpace(InputText))
                    {
                        InputText = text;
                        NotificationService.Instance.Notify("Portapapeles", "Enlace detectado y pegado automáticamente.", NotificationType.Info);
                    }
                }
            }
        }
        catch { }
    }

    private void SavePreferences()
    {
        _config.Set("format", SelectedFormat);
        _config.Set("bitrate", SelectedBitrate);
        _config.Set("max_concurrent", SelectedConcurrent);
        _config.Set("cookies_browser", SelectedBrowser);
        _config.Set("sponsorblock", IsSponsorBlock);
        _config.Set("normalize", IsNormalize);
        _config.Set("embed_lyrics", IsEmbedLyrics);
        _config.Set("enumerar", IsEnumerate);
        _config.Set("destination_folder", DestinationFolder);
        _config.Set("clipboard_monitor", IsClipboardMonitorEnabled);
        _config.Save();
    }

    [RelayCommand]
    private void SelectDestinationFolder()
    {
        var folder = DialogService.Instance.SelectFolder(DestinationFolder);
        if (!string.IsNullOrEmpty(folder))
        {
            DestinationFolder = folder;
            SavePreferences();
        }
    }

    [RelayCommand]
    public async Task LoadListFile()
    {
        var file = DialogService.Instance.SelectFile("Archivos de lista (*.txt;*.csv)|*.txt;*.csv|Todos los archivos (*.*)|*.*");
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;

        await LoadListFileFromPath(file);
    }

    public async Task LoadListFileFromPath(string file)
    {
        try
        {
            var content = File.ReadAllText(file);
            var tracks = Parsers.ParseSongList(content);
            if (!tracks.Any())
            {
                DialogService.Instance.ShowMessage("No se encontraron canciones válidas en el archivo seleccionado.", "Aviso", MessageBoxImage.Warning);
                return;
            }

            var entries = tracks.Select(t => new DownloadEntry
            {
                Title = t,
                Url = Helpers.IsUrl(t) ? t : null
            }).ToList();

            var playlistName = Path.GetFileNameWithoutExtension(file);
            var selected = DialogService.Instance.ShowTrackSelectionDialog(Path.GetFileName(file), $"Lista de {entries.Count} canciones", entries);
            if (selected != null && selected.Any())
            {
                await StartBatchDownloadAsync(selected, playlistName);
            }
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowMessage($"Error al leer lista: {ex.Message}", "Error", MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SearchAndAnalyze()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            DialogService.Instance.ShowMessage("Por favor ingrese un término de búsqueda o enlace.", "Aviso", MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusText = "Analizando entrada y buscando metadatos...";
        LogService.Instance.Info($"Búsqueda iniciada para: {InputText}");

        try
        {
            var isUrl = Helpers.IsUrl(InputText.Trim());
            var searchResult = await MetadataService.GetMetadataAsync(
                InputText.Trim(),
                isSearch: !isUrl,
                cookiesBrowser: SelectedBrowser,
                loggerCallback: msg => LogService.Instance.Info(msg),
                searchSource: SelectedSearchSource);

            if (searchResult.Entries.Count == 0)
            {
                DialogService.Instance.ShowMessage("No se encontraron resultados para la búsqueda.", "Sin resultados", MessageBoxImage.Information);
                StatusText = "Sin resultados";
                return;
            }

            if (searchResult.Type == "playlist")
            {
                var playlistTitle = string.IsNullOrWhiteSpace(searchResult.Title) ? "Lista_Descargas" : searchResult.Title;
                var selected = DialogService.Instance.ShowTrackSelectionDialog(
                    playlistTitle,
                    "Lista de reproducción detectada",
                    searchResult.Entries);

                if (selected != null && selected.Any())
                {
                    await StartBatchDownloadAsync(selected, playlistTitle);
                }
                else
                {
                    StatusText = "Operación cancelada por el usuario";
                }
            }
            else if (searchResult.Entries.Count > 1)
            {
                var selected = DialogService.Instance.ShowTrackSelectionDialog(
                    $"Resultados para: {InputText.Trim()}",
                    "Resultados de búsqueda",
                    searchResult.Entries);

                if (selected != null && selected.Any())
                {
                    if (selected.Count == 1)
                    {
                        await StartSingleDownloadAsync(selected[0]);
                    }
                    else
                    {
                        await StartBatchDownloadAsync(selected, playlistTitle: null);
                    }
                }
                else
                {
                    StatusText = "Operación cancelada por el usuario";
                }
            }
            else
            {
                await StartSingleDownloadAsync(searchResult.Entries[0]);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Error en análisis: {ex.Message}");
            DialogService.Instance.ShowMessage($"Error al analizar: {ex.Message}", "Error", MessageBoxImage.Error);
            StatusText = "Error en análisis";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubmitInput()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            DialogService.Instance.ShowMessage("Por favor ingrese un término de búsqueda o enlace.", "Aviso", MessageBoxImage.Warning);
            return;
        }

        await SearchAndAnalyze();
    }

    [RelayCommand]
    private async Task StartDownload()
    {
        await SubmitInput();
    }

    private async Task StartSingleDownloadAsync(DownloadEntry entry)
    {
        IsBusy = true;
        _downloadCts = new CancellationTokenSource();
        SavePreferences();

        var query = entry.Url ?? entry.Title ?? InputText;
        StatusText = $"Descargando: {entry.Title ?? query}...";
        ProgressPercentage = 0;
        ProgressDetail = "Iniciando descarga...";

        try
        {
            var result = await Task.Run(() => DownloadEngine.DownloadItem(
                query,
                SelectedFormat,
                SelectedBitrate,
                outFolder: DestinationFolder,
                isSearch: !Helpers.IsUrl(query),
                sponsorblock: IsSponsorBlock,
                cookiesBrowser: SelectedBrowser,
                normalize: IsNormalize,
                embedLyrics: IsEmbedLyrics,
                cancelEvent: _downloadCts.Token,
                loggerCallback: msg => LogService.Instance.Info(msg),
                isEnumerate: IsEnumerate,
                itemIndex: 0,
                searchSource: SelectedSearchSource,
                progressCallback: dict =>
                {
                    if (dict.TryGetValue("percent", out var pctStr) && double.TryParse(pctStr, out var pct))
                    {
                        ProgressPercentage = pct;
                    }
                    if (dict.TryGetValue("eta", out var eta) && dict.TryGetValue("speed", out var spd))
                    {
                        ProgressDetail = $"{pctStr}% | Velocidad: {spd} | ETA: {eta}";
                    }
                }));

            if (result.Success)
            {
                StatusText = "¡Descarga completada exitosamente!";
                ProgressPercentage = 100;
                ProgressDetail = "Completado al 100%";
                LogService.Instance.Info($"Descarga completada: {result.FilePath}");

                _historyManager.AddEntry(new DownloadEntry
                {
                    Title = entry.Title ?? Path.GetFileNameWithoutExtension(result.FilePath),
                    Url = entry.Url ?? query,
                    FilePath = result.FilePath,
                    Format = SelectedFormat,
                    Status = "OK",
                    Timestamp = DateTime.Now
                });

                NotificationService.Instance.NotifyDownloadComplete(entry.Title ?? Path.GetFileNameWithoutExtension(result.FilePath) ?? "Canción", result.FilePath ?? "");
            }
            else
            {
                StatusText = $"Error: {result.ErrorMessage ?? "Fallo en descarga"}";
                LogService.Instance.Error($"Fallo de descarga: {result.ErrorMessage}");
                NotificationService.Instance.NotifyError("Error en Descarga", result.ErrorMessage ?? "Fallo al procesar el archivo.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Descarga cancelada";
            LogService.Instance.Warn("Descarga cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            LogService.Instance.Error($"Error en proceso de descarga: {ex.Message}");
            NotificationService.Instance.NotifyError("Error en Descarga", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _downloadCts = null;
        }
    }

    private async Task StartBatchDownloadAsync(List<DownloadEntry> entries, string? playlistTitle = null)
    {
        IsBusy = true;
        _downloadCts = new CancellationTokenSource();
        SavePreferences();

        var batchTargetFolder = !string.IsNullOrWhiteSpace(playlistTitle)
            ? Path.Combine(DestinationFolder, Helpers.SanitizeFolderName(playlistTitle))
            : DestinationFolder;

        Directory.CreateDirectory(batchTargetFolder);

        var total = entries.Count;
        var completed = 0;
        var successCount = 0;
        var startedCount = 0;

        StatusText = $"Iniciando descarga de {total} canciones en '{Path.GetFileName(batchTargetFolder)}'...";
        ProgressPercentage = 0;
        ProgressDetail = $"0 de {total} completadas";
        LogService.Instance.Info($"Carpeta de destino para la lista: {batchTargetFolder}");

        var slotPool = new System.Collections.Concurrent.ConcurrentQueue<int>(Enumerable.Range(0, SelectedConcurrent));
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ActiveSlots.Clear();
            for (int i = 0; i < SelectedConcurrent; i++)
            {
                ActiveSlots.Add(new ActiveDownloadSlot { SlotNumber = i + 1, Title = "En espera...", IsActive = false });
            }
            HasActiveBatchSlots = true;
        });

        try
        {
            var throttler = new SemaphoreSlim(SelectedConcurrent);
            var tasks = entries.Select(async entry =>
            {
                await throttler.WaitAsync(_downloadCts.Token);
                var hasSlot = slotPool.TryDequeue(out var slotIdx);
                var slot = hasSlot && slotIdx < ActiveSlots.Count ? ActiveSlots[slotIdx] : null;

                try
                {
                    if (_downloadCts.Token.IsCancellationRequested) return;

                    var itemIndex = Interlocked.Increment(ref startedCount);
                    var itemTitle = entry.Title ?? "Canción";

                    if (slot != null)
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            slot.Title = itemTitle;
                            slot.Progress = 0;
                            slot.Speed = "...";
                            slot.Eta = "...";
                            slot.IsActive = true;
                        });
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        StatusText = $"[{itemIndex} de {total}] Descargando: {itemTitle}";
                    });

                    var query = entry.Url ?? entry.Title ?? "";
                    var res = await Task.Run(() => DownloadEngine.DownloadItem(
                        query,
                        SelectedFormat,
                        SelectedBitrate,
                        outFolder: batchTargetFolder,
                        isSearch: !Helpers.IsUrl(query),
                        sponsorblock: IsSponsorBlock,
                        cookiesBrowser: SelectedBrowser,
                        normalize: IsNormalize,
                        embedLyrics: IsEmbedLyrics,
                        cancelEvent: _downloadCts.Token,
                        loggerCallback: msg => LogService.Instance.Info(msg),
                        isEnumerate: IsEnumerate,
                        itemIndex: itemIndex,
                        searchSource: SelectedSearchSource,
                        progressCallback: dict =>
                        {
                            if (slot != null)
                            {
                                if (dict.TryGetValue("percent", out var pctStr) && double.TryParse(pctStr, out var pct))
                                {
                                    Application.Current?.Dispatcher.Invoke(() => slot.Progress = pct);
                                }
                                if (dict.TryGetValue("speed", out var spd))
                                {
                                    Application.Current?.Dispatcher.Invoke(() => slot.Speed = spd);
                                }
                                if (dict.TryGetValue("eta", out var eta))
                                {
                                    Application.Current?.Dispatcher.Invoke(() => slot.Eta = eta);
                                }
                            }
                        }));

                    var done = Interlocked.Increment(ref completed);
                    if (res.Success)
                    {
                        Interlocked.Increment(ref successCount);
                        _historyManager.AddEntry(new DownloadEntry
                        {
                            Title = entry.Title ?? Path.GetFileNameWithoutExtension(res.FilePath),
                            Url = entry.Url ?? query,
                            FilePath = res.FilePath,
                            Format = SelectedFormat,
                            Status = "OK",
                            Timestamp = DateTime.Now
                        });
                    }

                    var overallPct = Math.Round((double)done / total * 100, 1);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ProgressPercentage = overallPct;
                        ProgressDetail = $"Completadas: {done} de {total} ({overallPct}%)";
                        StatusText = $"[{done} de {total}] {(res.Success ? "Descargada con éxito" : "Error")}: {itemTitle}";
                    });
                }
                finally
                {
                    if (slot != null)
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            slot.IsActive = false;
                            slot.Title = "En espera";
                            slot.Progress = 0;
                            slot.Speed = string.Empty;
                            slot.Eta = string.Empty;
                        });
                        slotPool.Enqueue(slotIdx);
                    }
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
            StatusText = $"Lote completado: {successCount} de {total} descargadas correctamente.";
            ProgressPercentage = 100;
            ProgressDetail = $"Finalizado: {successCount} exitosas, {total - successCount} fallidas";

            NotificationService.Instance.NotifyBatchComplete(successCount, total);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Descarga por lotes cancelada.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error en lote: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _downloadCts = null;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                HasActiveBatchSlots = false;
                ActiveSlots.Clear();
            });
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
        {
            _downloadCts.Cancel();
            StatusText = "Cancelando descarga...";
        }
    }
}

public partial class ActiveDownloadSlot : ObservableObject
{
    [ObservableProperty]
    private int _slotNumber;

    [ObservableProperty]
    private string _title = "En espera...";

    [ObservableProperty]
    private double _progress = 0;

    [ObservableProperty]
    private string _speed = string.Empty;

    [ObservableProperty]
    private string _eta = string.Empty;

    [ObservableProperty]
    private bool _isActive = false;
}
