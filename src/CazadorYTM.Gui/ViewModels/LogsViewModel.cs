namespace CazadorYTM.Gui.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CazadorYTM.Gui.Services;

public partial class LogsViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 500;
    private readonly DispatcherTimer _updateTimer;
    private readonly Action<string, string> _logHandler;
    private bool _isDirty = false;
    private bool _disposed = false;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "Todos";

    public ObservableCollection<string> FilterOptions { get; } = new() { "Todos", "INFO", "WARN", "ERROR" };
    public ObservableCollection<string> Entries { get; } = new();

    public LogsViewModel()
    {
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _updateTimer.Tick += (s, e) =>
        {
            if (_isDirty)
            {
                _isDirty = false;
                UpdateFilteredText();
            }
        };
        _updateTimer.Start();

        _logHandler = (msg, lvl) =>
        {
            if (_disposed) return;
            var line = $"[{DateTime.Now:HH:mm:ss}] [{lvl.ToUpper()}] {msg}";
            
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                if (Entries.Count >= MaxLogEntries)
                {
                    Entries.RemoveAt(0);
                }
                Entries.Add(line);
                _isDirty = true;
            });
        };

        LogService.Instance.OnLogReceived += _logHandler;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LogService.Instance.OnLogReceived -= _logHandler;
        _updateTimer.Stop();
    }

    partial void OnSelectedFilterChanged(string value)
    {
        UpdateFilteredText();
    }

    private void UpdateFilteredText()
    {
        if (Entries.Count == 0)
        {
            LogText = string.Empty;
            return;
        }

        if (SelectedFilter == "Todos")
        {
            LogText = string.Join(Environment.NewLine, Entries);
        }
        else
        {
            var filtered = Entries.Where(e => e.Contains($"[{SelectedFilter}]", StringComparison.OrdinalIgnoreCase));
            LogText = string.Join(Environment.NewLine, filtered);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Entries.Clear();
        LogText = string.Empty;
        _isDirty = false;
    }

    [RelayCommand]
    private void CopyLogs()
    {
        if (!string.IsNullOrEmpty(LogText))
        {
            Clipboard.SetText(LogText);
            DialogService.Instance.ShowMessage("Registros copiados al portapapeles.", "Copiado");
        }
    }

    [RelayCommand]
    private void ExportLogs()
    {
        if (string.IsNullOrEmpty(LogText))
        {
            DialogService.Instance.ShowMessage("No hay registros para exportar.", "Aviso");
            return;
        }

        var path = DialogService.Instance.SaveFile($"cazador_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt", "Archivos de texto (*.txt)|*.txt");
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                File.WriteAllText(path, LogText, Encoding.UTF8);
                DialogService.Instance.ShowMessage("Registros exportados correctamente.", "Éxito");
            }
            catch (Exception ex)
            {
                DialogService.Instance.ShowMessage($"Error al exportar: {ex.Message}", "Error", MessageBoxImage.Error);
            }
        }
    }
}
