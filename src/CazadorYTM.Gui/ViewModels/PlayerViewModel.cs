namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class PlayerViewModel : ObservableObject
{
    private readonly IAudioPlayerService _player;
    private double _previousVolume = 0.8;
    private string? _remoteStreamUrl;

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
        if (string.IsNullOrEmpty(_player.CurrentFilePath))
        {
            TrackTitle = LocalizationService.Instance.Get("PlayerStatusNoActive", "Sin reproducción activa");
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
    }

    private void OnPlayerPositionChanged(TimeSpan pos, TimeSpan dur)
    {
        UpdatePositionTexts(pos, dur);

        PositionSeconds = pos.TotalSeconds;
        DurationSeconds = dur.TotalSeconds;
        HasPrevious = _player.HasPrevious;
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = value;
        UpdateVolumeTexts(value);

        try
        {
            var config = new AppConfig();
            config.Set("player_volume", value.ToString("F2", CultureInfo.InvariantCulture));
            config.Save();
        }
        catch { }
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
}
