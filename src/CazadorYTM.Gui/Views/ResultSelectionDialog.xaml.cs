namespace CazadorYTM.Gui.Views;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CazadorYTM.Gui.ViewModels;

public partial class ResultSelectionDialog : Window
{
    public ResultSelectionDialog()
    {
        InitializeComponent();
        Services.Win11ThemeHelper.ApplyWin11Backdrop(this, isDialog: true);
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Closed += (s, e) => (DataContext as ResultSelectionViewModel)?.Dispose();
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (TracksList.Items.Count > 0)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (TracksList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem container)
                {
                    var cb = FindVisualChild<CheckBox>(container);
                    cb?.Focus();
                }
                else
                {
                    TracksList.Focus();
                }
            }, DispatcherPriority.Input);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (((e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) ||
             e.Key == Key.Apps || e.SystemKey == Key.Apps) &&
            TracksList.IsKeyboardFocusWithin)
        {
            OpenTrackContextMenu();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (Keyboard.FocusedElement is not Button && DataContext is ResultSelectionViewModel vm)
            {
                vm.ConfirmCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (DataContext is ResultSelectionViewModel vm)
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OpenTrackContextMenu()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused != null &&
            ItemsControl.ContainerFromElement(TracksList, focused) is ListBoxItem container)
        {
            TracksList.SelectedItem = container.DataContext;
        }

        if (TracksList.ContextMenu == null)
        {
            return;
        }

        TracksList.ContextMenu.PlacementTarget = TracksList;
        TracksList.ContextMenu.IsOpen = true;
    }

    private void TracksList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(list, source) is ListBoxItem container)
        {
            list.SelectedItem = container.DataContext;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ResultSelectionViewModel oldVm)
        {
            oldVm.RequestClose -= HandleRequestClose;
        }
        if (e.NewValue is ResultSelectionViewModel newVm)
        {
            newVm.RequestClose += HandleRequestClose;
        }
    }

    private void HandleRequestClose(bool result)
    {
        (DataContext as ResultSelectionViewModel)?.Dispose();
        DialogResult = result;
        Close();
    }
}
