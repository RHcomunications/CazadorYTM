namespace CazadorYTM.Gui.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Views;
using CazadorYTM.Gui.ViewModels;

public interface IDialogService
{
    string? SelectFolder(string? initialDirectory = null);
    string? SelectFile(string filter, string? initialDirectory = null);
    string? SaveFile(string defaultFileName, string filter);
    void ShowMessage(string message, string title = "Cazador YTM", MessageBoxImage icon = MessageBoxImage.Information);
    bool ShowConfirmation(string message, string? title = null);
    List<DownloadEntry>? ShowTrackSelectionDialog(string title, string typeDescription, List<DownloadEntry> tracks);
    PlaylistSelection? ShowPlaylistPicker(string trackTitle, IReadOnlyList<RadarItem> playlists);
    DownloadFormatChoice? ShowDownloadFormatDialog(string trackTitle, string defaultFormat, string defaultBitrate);
}

public sealed record PlaylistSelection(string? PlaylistId, string? NewPlaylistTitle);

public sealed record DownloadFormatChoice(bool UseDefault, string Format, string Bitrate);

public class DialogService : IDialogService
{
    private static readonly Lazy<DialogService> _instance = new(() => new DialogService());
    public static DialogService Instance => _instance.Value;

    public string? SelectFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta de destino",
            InitialDirectory = !string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FolderName;
        }
        return null;
    }

    public string? SelectFile(string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar archivo",
            Filter = filter,
            InitialDirectory = !string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileName;
        }
        return null;
    }

    public string? SaveFile(string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar archivo",
            FileName = defaultFileName,
            Filter = filter
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileName;
        }
        return null;
    }

    public void ShowMessage(string message, string title = "Cazador YTM", MessageBoxImage icon = MessageBoxImage.Information)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, icon);
    }

    public bool ShowConfirmation(string message, string? title = null)
    {
        return MessageBox.Show(message, title ?? LocalizationService.Instance["ConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public List<DownloadEntry>? ShowTrackSelectionDialog(string title, string typeDescription, List<DownloadEntry> tracks)
    {
        var vm = new ResultSelectionViewModel(title, typeDescription, tracks);
        var dialog = new ResultSelectionDialog
        {
            DataContext = vm,
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            return vm.GetSelectedEntries();
        }
        return null;
    }

    public PlaylistSelection? ShowPlaylistPicker(string trackTitle, IReadOnlyList<RadarItem> playlists)
    {
        var dialog = new PlaylistPickerWindow(trackTitle, playlists)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Selection : null;
    }

    public DownloadFormatChoice? ShowDownloadFormatDialog(string trackTitle, string defaultFormat, string defaultBitrate)
    {
        var dialog = new DownloadFormatDialog(trackTitle, defaultFormat, defaultBitrate)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }
}
