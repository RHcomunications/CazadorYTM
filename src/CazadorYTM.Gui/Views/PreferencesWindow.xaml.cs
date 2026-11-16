namespace CazadorYTM.Gui.Views;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CazadorYTM.Gui.ViewModels;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        Services.Win11ThemeHelper.ApplyWin11Backdrop(this, isDialog: true);
        var vm = new PreferencesViewModel();
        DataContext = vm;
        vm.RequestClose += () => Close();

        Closed += (s, e) => vm.Dispose();

        Loaded += (s, e) => Dispatcher.InvokeAsync(() =>
        {
            if (PrefTabs.SelectedItem is TabItem item)
            {
                item.Focus();
            }
        }, DispatcherPriority.Input);

        PrefTabs.PreviewKeyDown += (s, e) =>
        {
            if (e.Key is Key.Down or Key.Enter && Keyboard.FocusedElement is TabItem)
            {
                e.Handled = true;
                (PrefTabs.SelectedContent as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        };

        BtnCancel.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                if (PrefTabs.SelectedItem is TabItem item)
                {
                    e.Handled = true;
                    item.Focus();
                }
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
}
