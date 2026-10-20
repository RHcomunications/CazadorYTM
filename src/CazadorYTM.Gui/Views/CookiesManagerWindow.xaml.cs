namespace CazadorYTM.Gui.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CazadorYTM.Gui.ViewModels;

public partial class CookiesManagerWindow : Window
{
    public CookiesManagerWindow()
    {
        InitializeComponent();
        var vm = new CookiesManagerViewModel();
        vm.RequestClose += () => Close();
        DataContext = vm;

        Loaded += (s, e) => Dispatcher.InvokeAsync(() =>
        {
            if (CookiesTabs.SelectedItem is TabItem item)
            {
                item.Focus();
            }
        }, DispatcherPriority.Input);
    }
}
