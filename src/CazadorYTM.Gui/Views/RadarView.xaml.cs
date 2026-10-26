namespace CazadorYTM.Gui.Views;

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.ViewModels;

public partial class RadarView : UserControl
{
    public RadarView()
    {
        InitializeComponent();
        DataContextChanged += RadarView_DataContextChanged;
    }

    private void RadarView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RadarViewModel oldVm)
        {
            oldVm.RequestOpenCookiesManager -= OpenCookiesDialog;
            oldVm.RequestFocusItem -= FocusGridAndItem;
        }

        if (e.NewValue is RadarViewModel newVm)
        {
            newVm.RequestOpenCookiesManager += OpenCookiesDialog;
            newVm.RequestFocusItem += FocusGridAndItem;
        }
    }

    private void FocusGridAndItem(RadarItem? item)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (GridRadar.Visibility != Visibility.Visible) return;

            GridRadar.Focus();
            var target = item ?? GridRadar.SelectedItem as RadarItem ?? (GridRadar.Items.Count > 0 ? GridRadar.Items[0] as RadarItem : null);
            if (target != null)
            {
                GridRadar.SelectedItem = target;
                GridRadar.ScrollIntoView(target);
                var row = (DataGridRow?)GridRadar.ItemContainerGenerator.ContainerFromItem(target);
                row?.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OpenCookiesDialog()
    {
        var win = new CookiesManagerWindow
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();
    }

    private void Category_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is RadioButton rb)
            {
                rb.IsChecked = true;
                e.Handled = true;
                FocusGridAndItem(null);
            }
        }
    }

    private void GridRadar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not RadarViewModel vm || vm.SelectedItem == null) return;

        if (vm.SelectedItem.IsPlaylist)
        {
            vm.OpenPlaylistCommand.Execute(vm.SelectedItem);
        }
        else
        {
            vm.PlayItemCommand.Execute(vm.SelectedItem);
        }
    }

    private void GridRadar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RadarViewModel vm) return;

        if (e.Key == Key.Right)
        {
            // Flecha Derecha en lista: abre y carga el contenido de la lista para explorar sus pistas
            if (vm.SelectedItem?.IsPlaylist == true)
            {
                e.Handled = true;
                vm.OpenPlaylistCommand.Execute(vm.SelectedItem);
            }
        }
        else if (e.Key == Key.Left || e.Key == Key.Back)
        {
            // Flecha Izquierda o Backspace: si se está dentro de una lista, vuelve a la vista de listas
            if (vm.IsViewingPlaylistDetails)
            {
                e.Handled = true;
                vm.BackToPlaylistsCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Enter)
        {
            // Las listas se reproducirán con enter, las canciones se reproducen con enter
            if (vm.SelectedItem != null)
            {
                e.Handled = true;
                if (vm.SelectedItem.IsPlaylist)
                {
                    vm.PlayPlaylistCommand.Execute(vm.SelectedItem);
                }
                else
                {
                    vm.PlayItemCommand.Execute(vm.SelectedItem);
                }
            }
        }
        else if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
                 (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.L)
            {
                e.Handled = true;
                vm.LikeSelectedCommand.Execute(null);
            }
            else if (e.Key == Key.D)
            {
                e.Handled = true;
                vm.DislikeSelectedCommand.Execute(null);
            }
            else if (e.Key == Key.A)
            {
                e.Handled = true;
                vm.AddToPlaylistCommand.Execute(null);
            }
        }
        else if (e.Key == Key.D &&
                 (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                 (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            if (vm.SelectedItem != null)
            {
                e.Handled = true;
                vm.SendToDownloadCommand.Execute(vm.SelectedItem);
            }
        }
    }
}
