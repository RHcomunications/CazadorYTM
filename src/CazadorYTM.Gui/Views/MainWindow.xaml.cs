namespace CazadorYTM.Gui.Views;

using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.Accessibility;
using CazadorYTM.Gui.ViewModels;

public partial class MainWindow : Window
{
    private IInputElement? _previousFocus;
    private bool _altKeyDown;
    private bool _otherKeyPressedWhileAlt;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        Services.Win11ThemeHelper.ApplyWin11Backdrop(this);

        AccessibilityAnnouncer.AnnouncementRequested += OnAnnouncementRequested;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        MainMenu.LostFocus += MainMenu_LostFocus;
        MainMenu.PreviewKeyDown += MainMenu_PreviewKeyDown;
        Closed += (s, e) => AccessibilityAnnouncer.AnnouncementRequested -= OnAnnouncementRequested;

        Loaded += (s, e) =>
        {
            Dispatcher.InvokeAsync(FocusSelectedTab, DispatcherPriority.Input);
        };
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Immediate close on Alt+F4
        if (e.SystemKey == Key.F4 || (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0))
        {
            e.Handled = true;
            Close();
            return;
        }

        // F10 immediately toggles main menu
        if (e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            ToggleMenu();
            e.Handled = true;
            return;
        }

        // Track bare Alt key press
        if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
        {
            _altKeyDown = true;
            _otherKeyPressedWhileAlt = false;
        }
        else if (_altKeyDown)
        {
            _otherKeyPressedWhileAlt = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Toggle menu only when standalone Alt key is released without combination
        if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
        {
            if (_altKeyDown && !_otherKeyPressedWhileAlt)
            {
                ToggleMenu();
                e.Handled = true;
            }
            _altKeyDown = false;
            _otherKeyPressedWhileAlt = false;
        }
    }

    private void ToggleMenu()
    {
        if (MainMenu.Visibility == Visibility.Collapsed)
        {
            _previousFocus = Keyboard.FocusedElement;
            MainMenu.Visibility = Visibility.Visible;
            
            Dispatcher.InvokeAsync(() =>
            {
                MenuArchivo.Focus();
            }, DispatcherPriority.Input);
        }
        else
        {
            HideMenu();
        }
    }

    private void MainMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideMenu();
            e.Handled = true;
        }
    }

    private void MainMenu_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!MainMenu.IsKeyboardFocusWithin)
            {
                HideMenu();
            }
        }, DispatcherPriority.Background);
    }

    private void HideMenu()
    {
        MainMenu.Visibility = Visibility.Collapsed;
        if (_previousFocus != null)
        {
            _previousFocus.Focus();
            _previousFocus = null;
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != MainTabs)
        {
            return;
        }

        Dispatcher.InvokeAsync(FocusSelectedTab, DispatcherPriority.Loaded);
    }

    private void FocusSelectedTab()
    {
        if (!IsLoaded || MainTabs.SelectedItem is not MainTabItem tab)
        {
            return;
        }

        if (MainTabs.ItemContainerGenerator.ContainerFromItem(tab) is not TabItem item)
        {
            AnnounceSelectedTab(tab);
            return;
        }

        var focused = item.Focus();
        var peer = UIElementAutomationPeer.FromElement(item)
                   ?? UIElementAutomationPeer.CreatePeerForElement(item);
        peer?.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);

        if (!focused)
        {
            AnnounceSelectedTab(tab);
        }
    }

    private void AnnounceSelectedTab(MainTabItem tab)
    {
        var index = MainTabs.Items.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var text = LocalizationService.Instance["TabAnnouncement"]
            .Replace("{0}", tab.Header)
            .Replace("{1}", (index + 1).ToString())
            .Replace("{2}", MainTabs.Items.Count.ToString());

        AccessibilityAnnouncer.Announce(text);
    }

    private void OnAnnouncementRequested(string message)
    {
        AppLiveAnnouncer.Text = message;
        LiveRegion.Raise(AppLiveAnnouncer);
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;

        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0) return;

                var txtFile = System.Linq.Enumerable.FirstOrDefault(files, f =>
                    f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

                if (txtFile != null)
                {
                    mainVm.NavigateToTabCommand.Execute("0");
                    await mainVm.DownloadVM.LoadListFileFromPath(txtFile);
                    return;
                }

                var audioFiles = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(files, f =>
                    f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)));

                if (audioFiles.Count > 0)
                {
                    mainVm.NavigateToTabCommand.Execute("2");
                    mainVm.PlayerVM.SetQueue(audioFiles, 0);
                    return;
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var text = (string)e.Data.GetData(DataFormats.Text);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    mainVm.NavigateToTabCommand.Execute("0");
                    mainVm.DownloadVM.InputText = text.Trim();
                }
            }
        }
        catch { }
    }

    private void ExitApp(object sender, RoutedEventArgs e)
    {
        Close();
    }
}