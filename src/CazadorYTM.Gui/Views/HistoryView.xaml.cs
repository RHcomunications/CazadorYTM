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

        GridHistory.PreviewKeyDown += GridHistory_PreviewKeyDown;
        GridHistory.MouseDoubleClick += GridHistory_MouseDoubleClick;
        GridHistory.SelectionChanged += GridHistory_SelectionChanged;
    }

    private void GridHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && GridHistory.SelectedItem is DownloadEntry entry)
        {
            vm.SelectedEntry = entry;
        }
    }

    private void GridHistory_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not HistoryViewModel vm) return;

        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

        if (!hasNavigationModifier && e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;

            var count = GridHistory.Items.Count;
            if (count == 0) return;

            var curIdx = GridHistory.SelectedIndex;
            var nextIdx = e.Key == Key.Down
                ? (curIdx < 0 ? 0 : System.Math.Min(curIdx + 1, count - 1))
                : (curIdx <= 0 ? 0 : curIdx - 1);

            if (nextIdx != curIdx || curIdx < 0)
            {
                GridHistory.SelectedIndex = nextIdx;
                if (GridHistory.Items[nextIdx] is DownloadEntry nextItem)
                {
                    vm.SelectedEntry = nextItem;
                    GridHistory.ScrollIntoView(nextItem);
                }
            }

            FocusSelectedRow(nextIdx);
            return;
        }

        if (!hasNavigationModifier && (e.Key is Key.Left or Key.Right))
        {
            // Evitar que las flechas horizontales saquen el foco de la fila hacia las pestañas superiores
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            var target = GridHistory.SelectedItem as DownloadEntry ?? vm.SelectedEntry;
            if (target != null)
            {
                vm.PlaySelected(target);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            var target = GridHistory.SelectedItem as DownloadEntry ?? vm.SelectedEntry;
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

    private void FocusSelectedRow(int index)
    {
        if (index < 0 || index >= GridHistory.Items.Count) return;

        GridHistory.UpdateLayout();
        if (GridHistory.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
        {
            row.Focus();
            Keyboard.Focus(row);
        }
    }

    private void GridHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HistoryViewModel vm)
        {
            var target = GridHistory.SelectedItem as DownloadEntry ?? vm.SelectedEntry;
            if (target != null)
            {
                vm.PlaySelected(target);
                e.Handled = true;
            }
        }
    }
}
