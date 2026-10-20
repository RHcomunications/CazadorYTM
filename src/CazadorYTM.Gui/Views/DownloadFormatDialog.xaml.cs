namespace CazadorYTM.Gui.Views;

using System.Windows;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Services;

public partial class DownloadFormatDialog : Window
{
    private readonly string _defaultFormat;
    private readonly string _defaultBitrate;

    public DownloadFormatDialog(string trackTitle, string defaultFormat, string defaultBitrate)
    {
        InitializeComponent();
        Win11ThemeHelper.ApplyWin11Backdrop(this, isDialog: true);

        _defaultFormat = defaultFormat;
        _defaultBitrate = defaultBitrate;

        var loc = LocalizationService.Instance;
        HeaderText.Text = loc["DownloadFormatHeader"].Replace("{0}", trackTitle);
        RbDefault.Content = loc["DownloadFormatUseDefault"]
            .Replace("{0}", defaultFormat)
            .Replace("{1}", defaultBitrate);

        CmbFormat.ItemsSource = Constants.FormatsDisplay;
        CmbBitrate.ItemsSource = Constants.Bitrates;
        CmbFormat.SelectedItem = System.Array.IndexOf(Constants.FormatsDisplay, defaultFormat) >= 0
            ? defaultFormat
            : Constants.FormatsDisplay[0];
        CmbBitrate.SelectedItem = System.Array.IndexOf(Constants.Bitrates, defaultBitrate) >= 0
            ? defaultBitrate
            : Constants.Bitrates[Constants.DefaultBitrateIndex];

        Loaded += (s, e) => RbDefault.Focus();
    }

    public DownloadFormatChoice? Choice { get; private set; }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (CustomPanel != null)
        {
            CustomPanel.IsEnabled = RbCustom.IsChecked == true;
        }
    }

    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        var useDefault = RbDefault.IsChecked == true;
        Choice = new DownloadFormatChoice(
            useDefault,
            useDefault ? _defaultFormat : (string?)CmbFormat.SelectedItem ?? _defaultFormat,
            useDefault ? _defaultBitrate : (string?)CmbBitrate.SelectedItem ?? _defaultBitrate);
        DialogResult = true;
    }
}
