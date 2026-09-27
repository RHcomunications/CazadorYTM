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
        if (e.Key == Key.Enter)
        {
            if (e.OriginalSource is not Button btn || (btn.Content?.ToString() != "Cancelar" && btn.Content?.ToString() != "_Cancelar"))
            {
                if (DataContext is ResultSelectionViewModel vm)
                {
                    vm.ConfirmCommand.Execute(null);
                    e.Handled = true;
                }
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
