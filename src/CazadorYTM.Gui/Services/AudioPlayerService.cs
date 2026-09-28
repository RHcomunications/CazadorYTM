namespace CazadorYTM.Gui.Services;

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

public enum PlaybackRepeatMode
{
    Off = 0,
    All = 1,
    One = 2
}

public interface IAudioPlayerService
{
    event Action? StateChanged;
    event Action<TimeSpan, TimeSpan>? PositionChanged;

    bool IsPlaying { get; }
    bool IsPaused { get; }
    string? CurrentFilePath { get; }
    double Volume { get; set; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }

    IReadOnlyList<string> Queue { get; }
    int CurrentQueueIndex { get; }
    bool HasNext { get; }
    bool HasPrevious { get; }
    bool IsShuffle { get; set; }
    PlaybackRepeatMode RepeatMode { get; set; }

    void Play(string filePath);
    void SetQueue(IEnumerable<string> tracks, int startIndex = 0);
    void PlayNext();
    void PlayPrevious();
    void Pause();
    void Resume();
    void Stop();
    void Seek(TimeSpan position);
}

public class AudioPlayerService : IAudioPlayerService
{
    private static readonly Lazy<AudioPlayerService> _instance = new(() => new AudioPlayerService());
    public static AudioPlayerService Instance => _instance.Value;

    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _timer = new();
    private readonly List<string> _queue = new();
    private readonly Random _random = new();

    public event Action? StateChanged;
    public event Action<TimeSpan, TimeSpan>? PositionChanged;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentFilePath { get; private set; }

    public IReadOnlyList<string> Queue => _queue.AsReadOnly();
    public int CurrentQueueIndex { get; private set; } = -1;

    public bool IsShuffle { get; set; } = false;
    public PlaybackRepeatMode RepeatMode { get; set; } = PlaybackRepeatMode.Off;

    public bool HasNext => _queue.Count > 0 && (CurrentQueueIndex < _queue.Count - 1 || RepeatMode == PlaybackRepeatMode.All || IsShuffle);
    public bool HasPrevious => _queue.Count > 0 && (CurrentQueueIndex > 0 || RepeatMode == PlaybackRepeatMode.All || Position.TotalSeconds > 3);

    public double Volume
    {
        get => _mediaPlayer.Volume;
        set
        {
            _mediaPlayer.Volume = Math.Clamp(value, 0.0, 1.0);
            StateChanged?.Invoke();
        }
    }

    public TimeSpan Position
    {
        get => _mediaPlayer.Position;
        set
        {
            _mediaPlayer.Position = value;
            PositionChanged?.Invoke(_mediaPlayer.Position, Duration);
        }
    }

    public TimeSpan Duration => _mediaPlayer.NaturalDuration.HasTimeSpan ? _mediaPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;

    public AudioPlayerService()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (s, e) =>
        {
            if (IsPlaying && !IsPaused)
            {
                PositionChanged?.Invoke(_mediaPlayer.Position, Duration);
            }
        };

        _mediaPlayer.MediaOpened += (s, e) =>
        {
            StateChanged?.Invoke();
            PositionChanged?.Invoke(TimeSpan.Zero, Duration);
        };

        _mediaPlayer.MediaEnded += (s, e) =>
        {
            OnTrackEnded();
        };

        _mediaPlayer.MediaFailed += (s, e) =>
        {
            Stop();
            LogService.Instance.Error($"Error al reproducir audio: {e.ErrorException?.Message ?? "Desconocido"}");
        };
    }

    private void OnTrackEnded()
    {
        if (RepeatMode == PlaybackRepeatMode.One)
        {
            Seek(TimeSpan.Zero);
            _mediaPlayer.Play();
            IsPlaying = true;
            IsPaused = false;
            _timer.Start();
            StateChanged?.Invoke();
            return;
        }

        if (HasNext)
        {
            PlayNext();
        }
        else
        {
            Stop();
        }
    }

    public void SetQueue(IEnumerable<string> tracks, int startIndex = 0)
    {
        _queue.Clear();
        _queue.AddRange(tracks.Where(File.Exists));

        if (_queue.Count > 0)
        {
            var idx = Math.Clamp(startIndex, 0, _queue.Count - 1);
            PlayTrackAtIndex(idx);
        }
        else
        {
            CurrentQueueIndex = -1;
            Stop();
        }
    }

    public void Play(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LogService.Instance.Error($"Archivo de audio no encontrado: {filePath}");
            return;
        }

        // Single track play adds/finds it in queue
        var existingIdx = _queue.IndexOf(filePath);
        if (existingIdx >= 0)
        {
            CurrentQueueIndex = existingIdx;
        }
        else
        {
            _queue.Clear();
            _queue.Add(filePath);
            CurrentQueueIndex = 0;
        }

        PlayInternal(filePath);
    }

    private void PlayTrackAtIndex(int index)
    {
        if (index >= 0 && index < _queue.Count)
        {
            CurrentQueueIndex = index;
            PlayInternal(_queue[index]);
        }
    }

    private void PlayInternal(string filePath)
    {
        try
        {
            _mediaPlayer.Close();
            CurrentFilePath = filePath;
            _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
            _mediaPlayer.Play();
            IsPlaying = true;
            IsPaused = false;
            _timer.Start();
            StateChanged?.Invoke();
            LogService.Instance.Info($"Reproduciendo: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"No se pudo abrir el archivo de audio: {ex.Message}");
            Stop();
        }
    }

    public void PlayNext()
    {
        if (_queue.Count == 0) return;

        if (IsShuffle && _queue.Count > 1)
        {
            var nextIdx = _random.Next(0, _queue.Count);
            if (nextIdx == CurrentQueueIndex)
                nextIdx = (nextIdx + 1) % _queue.Count;
            PlayTrackAtIndex(nextIdx);
            return;
        }

        if (CurrentQueueIndex < _queue.Count - 1)
        {
            PlayTrackAtIndex(CurrentQueueIndex + 1);
        }
        else if (RepeatMode == PlaybackRepeatMode.All)
        {
            PlayTrackAtIndex(0);
        }
        else
        {
            Stop();
        }
    }

    public void PlayPrevious()
    {
        if (_queue.Count == 0) return;

        // If played more than 3 seconds, replay current track from start
        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        if (CurrentQueueIndex > 0)
        {
            PlayTrackAtIndex(CurrentQueueIndex - 1);
        }
        else if (RepeatMode == PlaybackRepeatMode.All)
        {
            PlayTrackAtIndex(_queue.Count - 1);
        }
        else
        {
            Seek(TimeSpan.Zero);
        }
    }

    public void Pause()
    {
        if (IsPlaying && !IsPaused)
        {
            _mediaPlayer.Pause();
            IsPaused = true;
            _timer.Stop();
            StateChanged?.Invoke();
        }
    }

    public void Resume()
    {
        if (IsPlaying && IsPaused)
        {
            _mediaPlayer.Play();
            IsPaused = false;
            _timer.Start();
            StateChanged?.Invoke();
        }
    }

    public void Stop()
    {
        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Close();
        }
        catch { }

        _timer.Stop();
        IsPlaying = false;
        IsPaused = false;
        CurrentFilePath = null;
        StateChanged?.Invoke();
        PositionChanged?.Invoke(TimeSpan.Zero, TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        if (Duration > TimeSpan.Zero)
        {
            _mediaPlayer.Position = position;
            PositionChanged?.Invoke(position, Duration);
        }
    }
}
