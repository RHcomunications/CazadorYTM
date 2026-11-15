namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public enum SleepTimerOption
{
    Off = 0,
    Min15 = 15,
    Min30 = 30,
    Min45 = 45,
    Min60 = 60,
    EndOfTrack = -1
}

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlayerService _player;
    private double _previousVolume = 0.8;
    private string? _remoteStreamUrl;
    private bool _disposed;
    private System.Threading.Timer? _volumeSaveTimer;
    private readonly object _volumeSaveLock = new();

    // Lyrics fields
    private CancellationTokenSource? _lyricsCts;
    private string? _lastLyricsTrackPath;

    public ObservableCollection<LyricLine> Lyrics { get; } = new();

    [ObservableProperty]
    private int _currentLyricIndex = -1;

    [ObservableProperty]
    private bool _hasLyrics = false;

    [ObservableProperty]
    private bool _isLyricsLoading = false;

    [ObservableProperty]
    private bool _isLyricsVisible = false;

    [ObservableProperty]
    private string _lyricsStatusText = string.Empty;

    [ObservableProperty]
    private string _currentLyricLineText = string.Empty;

    // Playback Speed fields
    [ObservableProperty]
    private double _speedRatio = 1.0;

    [ObservableProperty]
    private string _speedRatioText = "1.00x";

    // Sleep Timer fields
    private DispatcherTimer? _sleepTimer;
    private DateTime _sleepDeadlineUtc;
    private TimeSpan _sleepRemaining = TimeSpan.Zero;
    private double _preFadeVolume = 0.8;
    private bool _sleepFadeApplied;

    [ObservableProperty]
    private SleepTimerOption _sleepTimerMode = SleepTimerOption.Off;

    [ObservableProperty]
    private string _sleepTimerText = string.Empty;

    [ObservableProperty]
    private bool _isSleepTimerActive = false;

    [ObservableProperty]
    private string _sleepTimerAnnouncement = string.Empty;

    [ObservableProperty]
    private string _trackTitle = LocalizationService.Instance.Get("PlayerStatusNoActive", "Sin reproducción activa");

    [ObservableProperty]
    private string _trackArtist = string.Empty;

    [ObservableProperty]
    private string _trackAnnouncement = string.Empty;

    [ObservableProperty]
    private string _currentPositionText = "00:00";

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private string _accessiblePositionText = "Posición: 00:00 de 00:00";

    [ObservableProperty]
    private string _accessibleVolumeText = "Volumen 80%";

    [ObservableProperty]
    private string _volumeAnnouncement = string.Empty;

    [ObservableProperty]
    private double _positionSeconds = 0;

    [ObservableProperty]
    private double _durationSeconds = 0;

    [ObservableProperty]
    private double _volume = 0.8;

    [ObservableProperty]
    private bool _isPlaying = false;

    [ObservableProperty]
    private bool _isPaused = false;

    [ObservableProperty]
    private bool _isShuffle = false;

    [ObservableProperty]
    private PlaybackRepeatMode _repeatMode = PlaybackRepeatMode.Off;

    [ObservableProperty]
    private string _repeatModeText = string.Empty;

    [ObservableProperty]
    private bool _hasNext = false;

    [ObservableProperty]
    private bool _hasPrevious = false;

    [ObservableProperty]
    private string _queueStatusText = string.Empty;

    public PlayerViewModel()
    {
        _player = AudioPlayerService.Instance;

        var config = new AppConfig();
        if (double.TryParse(config.Get("player_volume", "0.8"), NumberStyles.Float, CultureInfo.InvariantCulture, out var savedVol))
        {
            _volume = Math.Clamp(savedVol, 0.0, 1.0);
        }
        _player.Volume = _volume;
        UpdateVolumeTexts(_volume);
        UpdateRepeatModeText(_repeatMode);
        UpdatePositionTexts(TimeSpan.Zero, TimeSpan.Zero);
        UpdateSleepTimerText();

        SpeedRatio = _player.SpeedRatio;
        SpeedRatioText = $"{SpeedRatio.ToString("0.00", CultureInfo.InvariantCulture)}x";
        LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsEmpty", "Letras no cargadas");

        _player.StateChanged += OnPlayerStateChanged;
        _player.PositionChanged += OnPlayerPositionChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        UpdateVolumeTexts(Volume);
        UpdateRepeatModeText(RepeatMode);
        UpdateQueueStatusText();
        UpdatePositionTexts(TimeSpan.FromSeconds(PositionSeconds), TimeSpan.FromSeconds(DurationSeconds));
        UpdateSleepTimerText();

        if (HasLyrics)
        {
            LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsReady", "Letras sincronizadas");
        }
        else if (!IsLyricsLoading)
        {
            LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsEmpty", "Letras no cargadas");
        }

        if (string.IsNullOrEmpty(_player.CurrentFilePath))
        {
            TrackTitle = LocalizationService.Instance.Get("PlayerStatusNoActive", "Sin reproducción activa");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.StateChanged -= OnPlayerStateChanged;
        _player.PositionChanged -= OnPlayerPositionChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

        _sleepTimer?.Stop();
        _lyricsCts?.Cancel();
        _lyricsCts?.Dispose();

        lock (_volumeSaveLock)
        {
            if (_volumeSaveTimer != null)
            {
                _volumeSaveTimer.Dispose();
                _volumeSaveTimer = null;
                try
                {
                    var config = new AppConfig();
                    config.Set("player_volume", Volume.ToString("F2", CultureInfo.InvariantCulture));
                    config.Save();
                }
                catch { }
            }
        }
    }

    private void UpdateVolumeTexts(double vol)
    {
        var pct = (int)Math.Round(vol * 100);
        AccessibleVolumeText = pct == 0
            ? LocalizationService.Instance.Get("PlayerVolumeMuted", "Silenciado")
            : string.Format(LocalizationService.Instance.Get("PlayerVolumePercent", "Volumen {0} por ciento"), pct);
        VolumeAnnouncement = AccessibleVolumeText;
    }

    private void UpdateRepeatModeText(PlaybackRepeatMode mode)
    {
        RepeatModeText = mode switch
        {
            PlaybackRepeatMode.All => LocalizationService.Instance.Get("PlayerRepeatAll", "Repetir: Toda la lista"),
            PlaybackRepeatMode.One => LocalizationService.Instance.Get("PlayerRepeatOne", "Repetir: Esta canción"),
            _ => LocalizationService.Instance.Get("PlayerRepeatOff", "Repetir: Desactivado")
        };
    }

    private void UpdateQueueStatusText()
    {
        if (_player.Queue.Count > 1)
        {
            QueueStatusText = string.Format(LocalizationService.Instance.Get("PlayerTrackQueueStatus", "Pista {0} de {1}"), _player.CurrentQueueIndex + 1, _player.Queue.Count);
        }
        else
        {
            QueueStatusText = string.Empty;
        }
    }

    private void UpdatePositionTexts(TimeSpan pos, TimeSpan dur)
    {
        CurrentPositionText = Helpers.FormatDuration((int)pos.TotalSeconds);
        DurationText = Helpers.FormatDuration((int)dur.TotalSeconds);
        AccessiblePositionText = string.Format(LocalizationService.Instance.Get("PlayerPositionStatus", "Posición: {0} de {1}"), CurrentPositionText, DurationText);
    }

    private void OnPlayerStateChanged()
    {
        IsPlaying = _player.IsPlaying;
        IsPaused = _player.IsPaused;
        HasNext = _player.HasNext;
        HasPrevious = _player.HasPrevious;
        IsShuffle = _player.IsShuffle;
        RepeatMode = _player.RepeatMode;

        UpdateRepeatModeText(RepeatMode);
        UpdateQueueStatusText();

        var trackChanged = _lastLyricsTrackPath != _player.CurrentFilePath;

        if (!string.IsNullOrEmpty(_player.CurrentFilePath))
        {
            if (_remoteStreamUrl == _player.CurrentFilePath && !string.IsNullOrWhiteSpace(TrackTitle))
            {
                // Preserve explicitly provided remote track title and artist
            }
            else
            {
                _remoteStreamUrl = null;
                var filename = Path.GetFileName(_player.CurrentFilePath);
                var (title, artist) = MetadataHelpers.ExtractTitleArtist(filename);
                TrackTitle = string.IsNullOrWhiteSpace(title) ? filename : title;
                TrackArtist = artist;
            }
        }
        else
        {
            TrackTitle = LocalizationService.Instance.Get("PlayerStatusNoActive", "Sin reproducción activa");
            TrackArtist = string.Empty;
        }

        TrackAnnouncement = string.IsNullOrWhiteSpace(TrackArtist)
            ? TrackTitle
            : $"{TrackTitle} — {TrackArtist}";

        if (trackChanged)
        {
            _lastLyricsTrackPath = _player.CurrentFilePath;
            _lyricsCts?.Cancel();
            IsLyricsLoading = false;
            Lyrics.Clear();
            HasLyrics = false;
            CurrentLyricIndex = -1;
            CurrentLyricLineText = string.Empty;
            LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsEmpty", "Letras no cargadas");

            if (IsLyricsVisible && !string.IsNullOrEmpty(_player.CurrentFilePath))
            {
                _ = LoadLyricsForCurrentTrackAsync();
            }
        }
    }

    private void OnPlayerPositionChanged(TimeSpan pos, TimeSpan dur)
    {
        UpdatePositionTexts(pos, dur);

        PositionSeconds = pos.TotalSeconds;
        DurationSeconds = dur.TotalSeconds;
        HasPrevious = _player.HasPrevious;

        if (HasLyrics)
        {
            SyncLyricPosition(pos);
        }

        CheckSleepTimerOnTrackPosition(pos, dur);
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = value;
        UpdateVolumeTexts(value);

        lock (_volumeSaveLock)
        {
            _volumeSaveTimer?.Dispose();
            _volumeSaveTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    var config = new AppConfig();
                    config.Set("player_volume", value.ToString("F2", CultureInfo.InvariantCulture));
                    config.Save();
                }
                catch { }
            }, null, 500, System.Threading.Timeout.Infinite);
        }
    }

    [RelayCommand]
    public void VolumeUp()
    {
        Volume = Math.Clamp(Math.Round(Volume + 0.05, 2), 0.0, 1.0);
    }

    [RelayCommand]
    public void VolumeDown()
    {
        Volume = Math.Clamp(Math.Round(Volume - 0.05, 2), 0.0, 1.0);
    }

    [RelayCommand]
    public void MuteToggle()
    {
        if (Volume > 0.001)
        {
            _previousVolume = Volume;
            Volume = 0.0;
        }
        else
        {
            Volume = _previousVolume > 0.001 ? _previousVolume : 0.5;
        }
    }

    [RelayCommand]
    public void MaxVolume()
    {
        Volume = 1.0;
    }

    [RelayCommand]
    public void SeekRelative(string secondsStr)
    {
        if (double.TryParse(secondsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
        {
            var newPos = Math.Clamp(PositionSeconds + delta, 0.0, DurationSeconds);
            Seek(newPos);
        }
    }

    [RelayCommand]
    public void SeekToStart()
    {
        Seek(0.0);
    }

    [RelayCommand]
    public void SeekToEnd()
    {
        if (DurationSeconds > 0)
            Seek(DurationSeconds);
    }

    [RelayCommand]
    private void OpenFile()
    {
        var filter = LocalizationService.Instance.Get("AudioFilesFilter", "Archivos de audio (*.mp3;*.flac;*.m4a;*.opus;*.wav)|*.mp3;*.flac;*.m4a;*.opus;*.wav|Todos los archivos (*.*)|*.*");
        var file = DialogService.Instance.SelectFile(filter);
        if (!string.IsNullOrEmpty(file))
        {
            PlayTrack(file);
        }
    }

    [RelayCommand]
    public void PlayPause()
    {
        if (_player.IsPlaying)
        {
            if (_player.IsPaused)
                _player.Resume();
            else
                _player.Pause();
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _player.Stop();
        CurrentPositionText = "00:00";
        PositionSeconds = 0;
        AccessiblePositionText = string.Format(LocalizationService.Instance.Get("PlayerPositionStatus", "Posición: {0} de {1}"), "00:00", DurationText);
    }

    [RelayCommand]
    public void Seek(double position)
    {
        _player.Seek(TimeSpan.FromSeconds(position));
        CurrentPositionText = Helpers.FormatDuration((int)position);
        AccessiblePositionText = string.Format(LocalizationService.Instance.Get("PlayerPositionStatus", "Posición: {0} de {1}"), CurrentPositionText, DurationText);
    }

    [RelayCommand]
    public void PlayNext()
    {
        _player.PlayNext();
    }

    [RelayCommand]
    public void PlayPrevious()
    {
        _player.PlayPrevious();
    }

    [RelayCommand]
    public void ToggleShuffle()
    {
        _player.IsShuffle = !_player.IsShuffle;
        IsShuffle = _player.IsShuffle;
    }

    [RelayCommand]
    public void ToggleRepeat()
    {
        _player.RepeatMode = _player.RepeatMode switch
        {
            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
            PlaybackRepeatMode.All => PlaybackRepeatMode.One,
            _ => PlaybackRepeatMode.Off
        };
        RepeatMode = _player.RepeatMode;
        UpdateRepeatModeText(RepeatMode);
    }

    public void PlayTrack(string filePath)
    {
        _player.Play(filePath);
    }

    public void PlayRemoteTrack(string streamUrl, string title, string? artist = null)
    {
        _remoteStreamUrl = streamUrl;
        TrackTitle = title;
        TrackArtist = artist ?? string.Empty;
        TrackAnnouncement = string.IsNullOrWhiteSpace(TrackArtist)
            ? TrackTitle
            : $"{TrackTitle} — {TrackArtist}";
        _player.Play(streamUrl);
    }

    public void SetQueue(IEnumerable<string> tracks, int startIndex = 0)
    {
        _player.SetQueue(tracks, startIndex);
    }

    // ==========================================
    // Phase 1: Lyrics Handling
    // ==========================================

    partial void OnIsLyricsVisibleChanged(bool value)
    {
        if (value && Lyrics.Count == 0 && !IsLyricsLoading && !string.IsNullOrEmpty(_player.CurrentFilePath))
        {
            _ = LoadLyricsForCurrentTrackAsync();
        }
    }

    [RelayCommand]
    public void ToggleLyrics()
    {
        IsLyricsVisible = !IsLyricsVisible;
    }

    [RelayCommand]
    public async Task RefreshLyricsAsync()
    {
        await LoadLyricsForCurrentTrackAsync();
    }

    public async Task LoadLyricsForCurrentTrackAsync()
    {
        if (string.IsNullOrEmpty(_player.CurrentFilePath))
        {
            LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsEmpty", "Letras no cargadas");
            return;
        }

        _lyricsCts?.Cancel();
        _lyricsCts?.Dispose();
        _lyricsCts = new CancellationTokenSource();
        var ct = _lyricsCts.Token;

        IsLyricsLoading = true;
        LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsLoading", "Buscando letras...");

        try
        {
            var filePath = _player.CurrentFilePath;
            var title = TrackTitle;
            var artist = TrackArtist;
            var duration = (int)DurationSeconds;

            var result = await MetadataService.LoadLyricsAsync(
                filePath,
                title,
                string.IsNullOrWhiteSpace(artist) ? null : artist,
                duration > 0 ? duration : null,
                ct);

            if (ct.IsCancellationRequested) return;

            CurrentLyricIndex = -1;
            CurrentLyricLineText = string.Empty;
            Lyrics.Clear();
            foreach (var line in result)
            {
                Lyrics.Add(line);
            }

            HasLyrics = Lyrics.Count > 0;
            if (HasLyrics)
            {
                LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsReady", "Letras sincronizadas");
                SyncLyricPosition(TimeSpan.FromSeconds(PositionSeconds));
            }
            else
            {
                LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsNotFound", "No se encontraron letras");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested)
            {
                LyricsStatusText = LocalizationService.Instance.Get("PlayerLyricsError", "Error al cargar letras");
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLyricsLoading = false;
            }
        }
    }

    private void SyncLyricPosition(TimeSpan pos)
    {
        if (Lyrics.Count == 0) return;

        if (!Lyrics[0].IsSynchronized)
        {
            if (CurrentLyricIndex != 0)
            {
                CurrentLyricIndex = 0;
                CurrentLyricLineText = Lyrics[0].Text;
            }
            return;
        }

        var idx = -1;
        for (int i = 0; i < Lyrics.Count; i++)
        {
            if (Lyrics[i].Timestamp <= pos)
            {
                idx = i;
            }
            else
            {
                break;
            }
        }

        if (idx != CurrentLyricIndex)
        {
            CurrentLyricIndex = idx;
            CurrentLyricLineText = (idx >= 0 && idx < Lyrics.Count) ? Lyrics[idx].Text : string.Empty;
        }
    }

    // ==========================================
    // Phase 3: Playback Speed Control
    // ==========================================

    partial void OnSpeedRatioChanged(double value)
    {
        _player.SpeedRatio = value;
        SpeedRatioText = $"{value.ToString("0.00", CultureInfo.InvariantCulture)}x";
    }

    [RelayCommand]
    public void SpeedUp()
    {
        SpeedRatio = Math.Clamp(Math.Round(SpeedRatio + 0.25, 2), 0.75, 2.0);
    }

    [RelayCommand]
    public void SpeedDown()
    {
        SpeedRatio = Math.Clamp(Math.Round(SpeedRatio - 0.25, 2), 0.75, 2.0);
    }

    [RelayCommand]
    public void ResetSpeed()
    {
        SpeedRatio = 1.0;
    }

    [RelayCommand]
    public void CycleSpeed()
    {
        if (SpeedRatio < 1.0) SpeedRatio = 1.0;
        else if (SpeedRatio < 1.25) SpeedRatio = 1.25;
        else if (SpeedRatio < 1.5) SpeedRatio = 1.5;
        else if (SpeedRatio < 1.75) SpeedRatio = 1.75;
        else if (SpeedRatio < 2.0) SpeedRatio = 2.0;
        else SpeedRatio = 0.75;
    }

    // ==========================================
    // Phase 3: Sleep Timer with Soft Fade-Out
    // ==========================================

    partial void OnSleepTimerModeChanged(SleepTimerOption value)
    {
        _sleepTimer?.Stop();

        if (_sleepFadeApplied)
        {
            _sleepFadeApplied = false;
            Volume = _preFadeVolume;
        }

        if (value == SleepTimerOption.Off)
        {
            IsSleepTimerActive = false;
            UpdateSleepTimerText();
            SleepTimerAnnouncement = SleepTimerText;
        }
        else if (value == SleepTimerOption.EndOfTrack)
        {
            IsSleepTimerActive = true;
            _preFadeVolume = Volume;
            UpdateSleepTimerText();
            SleepTimerAnnouncement = SleepTimerText;
        }
        else
        {
            IsSleepTimerActive = true;
            _preFadeVolume = Volume;
            _sleepRemaining = TimeSpan.FromMinutes((int)value);
            _sleepDeadlineUtc = DateTime.UtcNow.Add(_sleepRemaining);
            UpdateSleepTimerText();
            SleepTimerAnnouncement = string.Format(LocalizationService.Instance.Get("PlayerSleepTimerStarted", "Temporizador activado por {0} minutos"), (int)value);

            if (_sleepTimer == null)
            {
                _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _sleepTimer.Tick += OnSleepTimerTick;
            }
            _sleepTimer.Start();
        }
    }

    private void OnSleepTimerTick(object? sender, EventArgs e)
    {
        if (SleepTimerMode == SleepTimerOption.Off || SleepTimerMode == SleepTimerOption.EndOfTrack)
        {
            _sleepTimer?.Stop();
            return;
        }

        _sleepRemaining = _sleepDeadlineUtc - DateTime.UtcNow;
        if (_sleepRemaining <= TimeSpan.FromSeconds(5) && _sleepRemaining > TimeSpan.Zero)
        {
            if (!_sleepFadeApplied) _preFadeVolume = Volume;
            _sleepFadeApplied = true;
            Volume = Math.Min(Volume, _preFadeVolume * (_sleepRemaining.TotalSeconds / 5.0));
        }
        else if (_sleepRemaining <= TimeSpan.Zero)
        {
            _sleepTimer?.Stop();
            _player.Pause();
            SleepTimerMode = SleepTimerOption.Off;
            SleepTimerAnnouncement = LocalizationService.Instance.Get("PlayerSleepTimerFired", "Temporizador de apagado completado. Reproducción pausada.");
            return;
        }

        UpdateSleepTimerText();
    }

    private void CheckSleepTimerOnTrackPosition(TimeSpan pos, TimeSpan dur)
    {
        if (SleepTimerMode == SleepTimerOption.EndOfTrack && dur > TimeSpan.Zero)
        {
            var remaining = dur - pos;
            if (remaining <= TimeSpan.FromMilliseconds(500) || pos >= dur)
            {
                _player.Pause();
                SleepTimerMode = SleepTimerOption.Off;
                SleepTimerAnnouncement = LocalizationService.Instance.Get("PlayerSleepTimerFired", "Temporizador de apagado completado. Reproducción pausada.");
            }
            else if (remaining <= TimeSpan.FromSeconds(4))
            {
                if (!_sleepFadeApplied) _preFadeVolume = Volume;
                _sleepFadeApplied = true;
                Volume = Math.Min(Volume, _preFadeVolume * (remaining.TotalSeconds / 4.0));
            }
            else if (_sleepFadeApplied)
            {
                _sleepFadeApplied = false;
                Volume = _preFadeVolume;
            }
        }
    }

    private void UpdateSleepTimerText()
    {
        if (SleepTimerMode == SleepTimerOption.Off)
        {
            SleepTimerText = LocalizationService.Instance.Get("PlayerSleepTimerOff", "Temporizador: Desactivado");
        }
        else if (SleepTimerMode == SleepTimerOption.EndOfTrack)
        {
            SleepTimerText = LocalizationService.Instance.Get("PlayerSleepTimerEndOfTrack", "Apagar al final de la pista");
        }
        else
        {
            var mins = (int)_sleepRemaining.TotalMinutes;
            var secs = _sleepRemaining.Seconds;
            SleepTimerText = string.Format(LocalizationService.Instance.Get("PlayerSleepTimerRemaining", "Apagado en {0}:{1:D2}"), mins, secs);
        }
    }

    [RelayCommand]
    public void CycleSleepTimer()
    {
        SleepTimerMode = SleepTimerMode switch
        {
            SleepTimerOption.Off => SleepTimerOption.Min15,
            SleepTimerOption.Min15 => SleepTimerOption.Min30,
            SleepTimerOption.Min30 => SleepTimerOption.Min45,
            SleepTimerOption.Min45 => SleepTimerOption.Min60,
            SleepTimerOption.Min60 => SleepTimerOption.EndOfTrack,
            _ => SleepTimerOption.Off
        };
    }

    [RelayCommand]
    public void SetSleepTimer(string minutesStr)
    {
        if (int.TryParse(minutesStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            SleepTimerMode = minutes switch
            {
                -1 => SleepTimerOption.EndOfTrack,
                15 => SleepTimerOption.Min15,
                30 => SleepTimerOption.Min30,
                45 => SleepTimerOption.Min45,
                60 => SleepTimerOption.Min60,
                _ => SleepTimerOption.Off
            };
        }
    }

    [RelayCommand]
    public void CancelSleepTimer()
    {
        SleepTimerMode = SleepTimerOption.Off;
    }
}
