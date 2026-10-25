namespace CazadorYTM.Gui.Views;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CazadorYTM.Core.Services;

public record ShortcutItem(string Shortcut, string Description);

public partial class ShortcutsDialog : Window
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        Services.Win11ThemeHelper.ApplyWin11Backdrop(this, isDialog: true);

        var loc = LocalizationService.Instance;
        var shortcuts = new List<ShortcutItem>
        {
            // Globales
            new("Ctrl + L", loc["ScOpenTable"]),
            new("Ctrl + 1", loc["ScOpenDownloads"]),
            new("Ctrl + 2", loc["ScOpenLibrary"]),
            new("Ctrl + 3", loc["ScOpenPlayer"]),
            new(loc["ScKeyRadar"], loc["ScOpenRadar"]),
            new("Ctrl + W", loc["ScCloseTab"]),
            new(loc["ScKeyContextMenu"], loc["ScContextMenu"]),
            new("Ctrl + ,", loc["ScOpenPreferences"]),
            new("Ctrl + S", loc["ScOpenDownloadsFolder"]),
            new(loc["ScKeyMenuBar"], loc["ScMenuBar"]),

            // Reproductor
            new(loc["ScKeySpace"], loc["ScPlayPause"]),
            new(loc["ScKeyMediaPlayPause"], loc["ScPlayPause"]),
            new(loc["ScKeyShiftLeft"], loc["ScBack5"]),
            new(loc["ScKeyShiftRight"], loc["ScForward5"]),
            new(loc["ScKeyCtrlShiftLeft"], loc["ScBack30"]),
            new(loc["ScKeyCtrlShiftRight"], loc["ScForward30"]),
            new(loc["ScKeyCtrlUp"], loc["ScVolumeUp"]),
            new(loc["ScKeyCtrlDown"], loc["ScVolumeDown"]),
            new(loc["ScKeyCtrlShiftDown"], loc["ScMute"]),
            new(loc["ScKeyCtrlShiftUp"], loc["ScVolumeMax"]),
            new("Ctrl + Shift + S", loc["ScStop"]),
            new(loc["ScKeyHome"], loc["ScGoStart"]),
            new(loc["ScKeyEnd"], loc["ScGoEnd"]),
            new(loc["ScKeyOpenAudioInPlayer"], loc["ScOpenAudio"]),

            // Descargas
            new(loc["ScKeySearch"], loc["ScSearchDownload"]),
            new(loc["ScKeyLoadList"], loc["ScLoadList"]),
            new(loc["ScKeySpaceResults"], loc["ScToggleTrack"]),

            // Biblioteca
            new(loc["ScKeyEnterLibrary"], loc["ScPlaySelected"]),
            new(loc["ScKeyDeleteLibrary"], loc["ScDeleteSelected"]),
            new(loc["ScKeyRefreshLibrary"], loc["ScRefreshHistory"]),

            // Radar YTM
            new(loc["ScKeyRight"], loc["ScRadarOpenPlaylist"]),
            new(loc["ScKeyLeftBack"], loc["ScRadarBackToPlaylists"]),
            new("Ctrl + D", loc["ScRadarSendDownload"]),
            new("Ctrl + K", loc["ScRadarManageCookies"]),
            new("Ctrl + Shift + L", loc["ScRadarLike"]),
            new("Ctrl + Shift + D", loc["ScRadarDislike"]),
            new("Ctrl + Shift + A", loc["ScRadarAddToPlaylist"])
        };

        LstShortcuts.ItemsSource = shortcuts;

        Loaded += (s, e) =>
        {
            LstShortcuts.Focus();
            if (shortcuts.Count > 0)
            {
                LstShortcuts.SelectedIndex = 0;
            }
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
