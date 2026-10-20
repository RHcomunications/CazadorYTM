namespace CazadorYTM.Gui.Views;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class PlaylistPickerWindow : Window
{
    public PlaylistPickerWindow(string trackTitle, IReadOnlyList<RadarItem> playlists)
    {
        InitializeComponent();
        Win11ThemeHelper.ApplyWin11Backdrop(this, isDialog: true);

        HeaderText.Text = LocalizationService.Instance["AddToPlaylistHeader"]
            .Replace("{0}", trackTitle);
        LstPlaylists.ItemsSource = playlists;
        EmptyText.Visibility = playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateButtons();
        Loaded += OnLoaded;
    }

    public PlaylistSelection? Selection { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (LstPlaylists.Items.Count > 0)
        {
            LstPlaylists.SelectedIndex = 0;
            LstPlaylists.Focus();
        }
        else
        {
            TxtNewPlaylist.Focus();
        }
    }

    private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnNewTitleChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

    private void OnNewTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BtnCreate.IsEnabled)
        {
            e.Handled = true;
            CreatePlaylist();
        }
    }

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BtnAdd.IsEnabled)
        {
            AddExisting();
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddExisting();

    private void OnCreateClick(object sender, RoutedEventArgs e) => CreatePlaylist();

    private void AddExisting()
    {
        if (LstPlaylists.SelectedItem is RadarItem playlist && !string.IsNullOrWhiteSpace(playlist.Id))
        {
            CloseWith(new PlaylistSelection(playlist.Id, null));
        }
    }

    private void CreatePlaylist()
    {
        var title = TxtNewPlaylist.Text.Trim();
        if (title.Length == 0)
        {
            return;
        }

        CloseWith(new PlaylistSelection(null, title));
    }

    private void CloseWith(PlaylistSelection selection)
    {
        Selection = selection;
        DialogResult = true;
    }

    private void UpdateButtons()
    {
        if (BtnAdd == null || BtnCreate == null)
        {
            return;
        }

        BtnAdd.IsEnabled = LstPlaylists.SelectedItem is RadarItem;
        BtnCreate.IsEnabled = !string.IsNullOrWhiteSpace(TxtNewPlaylist.Text);
    }
}
