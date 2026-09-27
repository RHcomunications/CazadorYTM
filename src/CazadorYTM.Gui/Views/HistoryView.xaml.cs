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
        if (e.Key == Key.Enter || e.Key == Key.Return)
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
