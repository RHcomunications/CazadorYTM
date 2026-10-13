namespace CazadorYTM.Gui.Views;

using System.Windows;
using CazadorYTM.Gui.ViewModels;

public partial class CookiesManagerWindow : Window
{
    public CookiesManagerWindow()
    {
        InitializeComponent();
        var vm = new CookiesManagerViewModel();
        vm.RequestClose += () => Close();
        DataContext = vm;
    }
}
