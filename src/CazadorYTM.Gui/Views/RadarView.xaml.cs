namespace CazadorYTM.Gui.Views;

using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Gui.ViewModels;

public partial class RadarView : UserControl
{
    public RadarView()
    {
        InitializeComponent();
    }

    private void GridRadar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RadarViewModel vm && vm.SelectedItem != null)
        {
            vm.PlayItemCommand.Execute(vm.SelectedItem);
        }
    }

    private void GridRadar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RadarViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.PlayItemCommand.Execute(vm.SelectedItem);
        }
        else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            vm.SendToDownloadCommand.Execute(vm.SelectedItem);
        }
    }
}
