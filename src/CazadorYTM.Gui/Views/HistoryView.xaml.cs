namespace CazadorYTM.Gui.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Core.Models;
using CazadorYTM.Gui.Accessibility;
using CazadorYTM.Gui.ViewModels;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    public void FocusListAndItem(DownloadEntry? entry = null) =>
        ListBoxNavigationHelper.FocusListAndItem(ListHistory, entry);

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

        ListBoxNavigationHelper.SyncSelectionFocus(ListHistory);
    }

    private void ListHistory_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not HistoryViewModel vm) return;

        if (ListBoxNavigationHelper.HandleUpDownKey(ListHistory, e)) return;

        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

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
