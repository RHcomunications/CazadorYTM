namespace CazadorYTM.Gui.Services;

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CazadorYTM.Core;

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
    private static readonly Lazy<AudioPlayerService> _instance = new(() =>
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() => new AudioPlayerService());
        }
        return new AudioPlayerService();
    });
    public static AudioPlayerService Instance => _instance.Value;

    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _timer = new();
    private readonly List<string> _queue = new();
    private readonly Random _random = new();

    private readonly AudioTranscodeCache _transcodeCache = new();

    private bool _isFallbackAttempt;

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

        _mediaPlayer.MediaFailed += async (s, e) =>
        {
            var failedPath = CurrentFilePath;
            if (failedPath != null && !_isFallbackAttempt)
            {
                _isFallbackAttempt = true;
                var transcoded = await _transcodeCache.TryTranscodeToTempWavAsync(failedPath);
                if (transcoded != null && CurrentFilePath == failedPath)
                {
                    LogService.Instance.Info($"Reproduciendo mediante decodificador universal FFmpeg: {Path.GetFileName(failedPath)}");
                    _mediaPlayer.Open(new Uri(transcoded, UriKind.Absolute));
                    _mediaPlayer.Play();
                    return;
                }
            }
            _isFallbackAttempt = false;
            Stop();
            LogService.Instance.Error($"Error al reproducir audio: {e.ErrorException?.Message ?? "Formato o códec no soportado"}");
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
        var isRemote = Helpers.IsUrl(filePath);
        if (!isRemote && !File.Exists(filePath))
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

    private async void PlayInternal(string filePath)
    {
        try
        {
            _isFallbackAttempt = false;
            _mediaPlayer.Close();
            CurrentFilePath = filePath;

            var isRemote = Helpers.IsUrl(filePath);
            var ext = isRemote ? string.Empty : Path.GetExtension(filePath).ToLowerInvariant();
            var targetUri = filePath;

            // Direct transcode for formats notoriously unsupported by default Windows Media Foundation (.opus, .ogg, .webm)
            if (!isRemote && (ext == ".opus" || ext == ".ogg" || ext == ".webm"))
            {
                var transcoded = await _transcodeCache.TryTranscodeToTempWavAsync(filePath);
                if (transcoded != null)
                {
                    targetUri = transcoded;
                    _isFallbackAttempt = true;
                }
            }

            if (CurrentFilePath != filePath) return;

            _mediaPlayer.Open(new Uri(targetUri, UriKind.Absolute));
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

        _isFallbackAttempt = false;
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

    public string? TryTranscodeToTempWav(string sourcePath) => _transcodeCache.TryTranscodeToTempWav(sourcePath);

    public async Task<string?> TryTranscodeToTempWavAsync(string sourcePath, System.Threading.CancellationToken cancellationToken = default) =>
        await _transcodeCache.TryTranscodeToTempWavAsync(sourcePath, cancellationToken);

    public void CleanupAllTempCache() => _transcodeCache.CleanupAllTempCache();
}
