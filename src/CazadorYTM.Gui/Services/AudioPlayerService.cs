namespace CazadorYTM.Gui.Services;

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

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

    void Play(string filePath);
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

    public event Action? StateChanged;
    public event Action<TimeSpan, TimeSpan>? PositionChanged;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentFilePath { get; private set; }

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
            Stop();
        };

        _mediaPlayer.MediaFailed += (s, e) =>
        {
            Stop();
            LogService.Instance.Error($"Error al reproducir audio: {e.ErrorException.Message}");
        };
    }

    public void Play(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LogService.Instance.Error($"Archivo de audio no encontrado: {filePath}");
            return;
        }

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
