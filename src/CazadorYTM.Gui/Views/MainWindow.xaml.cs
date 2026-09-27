namespace CazadorYTM.Gui.Views;

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        MainMenu.LostFocus += MainMenu_LostFocus;
        MainMenu.PreviewKeyDown += MainMenu_PreviewKeyDown;

        Loaded += (s, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                ViewDescarga.TxtInput.Focus();
            }, DispatcherPriority.Input);
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

        // F10 immediately toggles accessibility menu
        if (e.Key == Key.F10)
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

    private void ExitApp(object sender, RoutedEventArgs e)
    {
        Close();
    }
}