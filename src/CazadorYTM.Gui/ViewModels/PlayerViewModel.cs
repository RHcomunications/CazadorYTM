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

    [ObservableProperty]
    private string _trackTitle = "Sin reproducción activa";

    [ObservableProperty]
    private string _trackArtist = string.Empty;

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
    private string _repeatModeText = "Repetición: Desactivada";

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
        var pct = (int)Math.Round(_volume * 100);
        _accessibleVolumeText = pct == 0 ? "Silenciado" : $"Volumen {pct} por ciento";
        _volumeAnnouncement = _accessibleVolumeText;

        _player.StateChanged += OnPlayerStateChanged;
        _player.PositionChanged += OnPlayerPositionChanged;
    }

    private void OnPlayerStateChanged()
    {
        IsPlaying = _player.IsPlaying;
        IsPaused = _player.IsPaused;
        HasNext = _player.HasNext;
        HasPrevious = _player.HasPrevious;
        IsShuffle = _player.IsShuffle;
        RepeatMode = _player.RepeatMode;

        RepeatModeText = RepeatMode switch
        {
            PlaybackRepeatMode.All => "Repetir: Toda la lista",
            PlaybackRepeatMode.One => "Repetir: Esta canción",
            _ => "Repetir: Desactivado"
        };

        if (_player.Queue.Count > 1)
        {
            QueueStatusText = $"Pista {_player.CurrentQueueIndex + 1} de {_player.Queue.Count}";
        }
        else
        {
            QueueStatusText = string.Empty;
        }

        if (!string.IsNullOrEmpty(_player.CurrentFilePath))
        {
            var filename = Path.GetFileName(_player.CurrentFilePath);
            var (title, artist) = MetadataService.ExtractTitleArtistFromFilename(filename);
            TrackTitle = string.IsNullOrWhiteSpace(title) ? filename : title;
            TrackArtist = artist;
        }
        else
        {
            TrackTitle = "Sin reproducción activa";
            TrackArtist = string.Empty;
        }
    }

    private void OnPlayerPositionChanged(TimeSpan pos, TimeSpan dur)
    {
        CurrentPositionText = Helpers.FormatDuration((int)pos.TotalSeconds);
        DurationText = Helpers.FormatDuration((int)dur.TotalSeconds);
        AccessiblePositionText = $"Posición: {CurrentPositionText} de {DurationText}";

        PositionSeconds = pos.TotalSeconds;
        DurationSeconds = dur.TotalSeconds;
        HasPrevious = _player.HasPrevious;
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = value;
        var pct = (int)Math.Round(value * 100);
        AccessibleVolumeText = pct == 0 ? "Silenciado" : $"Volumen {pct} por ciento";
        VolumeAnnouncement = AccessibleVolumeText;

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
        var file = DialogService.Instance.SelectFile("Archivos de audio (*.mp3;*.flac;*.m4a;*.opus;*.wav)|*.mp3;*.flac;*.m4a;*.opus;*.wav|Todos los archivos (*.*)|*.*");
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
        AccessiblePositionText = $"Posición: 00:00 de {DurationText}";
    }

    [RelayCommand]
    public void Seek(double position)
    {
        _player.Seek(TimeSpan.FromSeconds(position));
        CurrentPositionText = Helpers.FormatDuration((int)position);
        AccessiblePositionText = $"Posición: {CurrentPositionText} de {DurationText}";
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
        RepeatModeText = RepeatMode switch
        {
            PlaybackRepeatMode.All => "Repetir: Toda la lista",
            PlaybackRepeatMode.One => "Repetir: Esta canción",
            _ => "Repetir: Desactivado"
        };
    }

    public void PlayTrack(string filePath)
    {
        _player.Play(filePath);
    }

    public void SetQueue(IEnumerable<string> tracks, int startIndex = 0)
    {
        _player.SetQueue(tracks, startIndex);
    }
}
