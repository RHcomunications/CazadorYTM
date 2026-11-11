namespace CazadorYTM.Gui.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Core.Models;
using CazadorYTM.Gui.ViewModels;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    public void FocusListAndItem(DownloadEntry? entry = null)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (ListHistory.Visibility != Visibility.Visible) return;

            var target = entry ?? ListHistory.SelectedItem as DownloadEntry ?? (ListHistory.Items.Count > 0 ? ListHistory.Items[0] as DownloadEntry : null);
            if (target == null)
            {
                ListHistory.Focus();
                return;
            }

            var index = ListHistory.Items.IndexOf(target);
            if (index < 0) index = 0;

            ListHistory.SelectedIndex = index;
            FocusItemContainer(index);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FocusItemContainer(int index)
    {
        if (index < 0 || index >= ListHistory.Items.Count) return;

        var item = ListHistory.Items[index];
        ListHistory.ScrollIntoView(item);

        if (ListHistory.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem lbi)
        {
            lbi.Focus();
            Keyboard.Focus(lbi);
        }
        else
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (index < ListHistory.Items.Count &&
                    ListHistory.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem retryLbi)
                {
                    retryLbi.Focus();
                    Keyboard.Focus(retryLbi);
                }
                else
                {
                    ListHistory.Focus();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void TxtSearchHistory_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Enter)
        {
            e.Handled = true;
            FocusListAndItem(null);
        }
    }

    private void ListHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && ListHistory.SelectedItem is DownloadEntry entry && !ReferenceEquals(vm.SelectedEntry, entry))
        {
            vm.SelectedEntry = entry;
        }

        if (ListHistory.SelectedIndex >= 0 && ListHistory.IsKeyboardFocusWithin)
        {
            FocusItemContainer(ListHistory.SelectedIndex);
        }
    }

    private void ListHistory_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not HistoryViewModel vm) return;

        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

        if (!hasNavigationModifier && e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;

            var count = ListHistory.Items.Count;
            if (count == 0) return;

            var curIdx = ListHistory.SelectedIndex;
            if (curIdx < 0)
            {
                ListHistory.SelectedIndex = 0;
                FocusItemContainer(0);
                return;
            }

            if (e.Key == Key.Up)
            {
                if (curIdx == 0)
                {
                    FocusItemContainer(0);
                    return;
                }

                var nextIdx = curIdx - 1;
                ListHistory.SelectedIndex = nextIdx;
                FocusItemContainer(nextIdx);
                return;
            }

            if (e.Key == Key.Down)
            {
                if (curIdx >= count - 1)
                {
                    FocusItemContainer(count - 1);
                    return;
                }

                var nextIdx = curIdx + 1;
                ListHistory.SelectedIndex = nextIdx;
                FocusItemContainer(nextIdx);
                return;
            }
        }

        if (!hasNavigationModifier && (e.Key is Key.Left or Key.Right))
        {
            // Evitar que las flechas horizontales saquen el foco de la fila hacia las pestañas superiores
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            var target = ListHistory.SelectedItem as DownloadEntry ?? vm.SelectedEntry;
            if (target != null)
            {
                vm.PlaySelected(target);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            var target = ListHistory.SelectedItem as DownloadEntry ?? vm.SelectedEntry;
            if (target != null)
            {
                vm.DeleteSelected(target);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5)
        {
            vm.RefreshHistory();
            e.Handled = true;
        }
    }

    private void ListHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HistoryViewModel vm)
        {
            var target = ListHistory.SelectedItem as DownloadEntry ?? vm.SelectedEntry;
            if (target != null)
            {
                vm.PlaySelected(target);
                e.Handled = true;
            }
        }
    }
}
