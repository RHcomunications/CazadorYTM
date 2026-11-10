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
            oldVm.RequestFocusItem -= FocusListAndItem;
        }

        if (e.NewValue is RadarViewModel newVm)
        {
            newVm.RequestOpenCookiesManager += OpenCookiesDialog;
            newVm.RequestFocusItem += FocusListAndItem;
        }
    }

    private void FocusListAndItem(RadarItem? item)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (ListRadar.Visibility != Visibility.Visible) return;

            var target = item ?? ListRadar.SelectedItem as RadarItem ?? (ListRadar.Items.Count > 0 ? ListRadar.Items[0] as RadarItem : null);
            if (target == null)
            {
                ListRadar.Focus();
                return;
            }

            ListRadar.SelectedItem = target;
            ListRadar.ScrollIntoView(target);

            if (ListRadar.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem lbi)
            {
                lbi.Focus();
                Keyboard.Focus(lbi);
            }
            else
            {
                ListRadar.Focus();
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

    private void TxtRadarFilter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Enter)
        {
            e.Handled = true;
            FocusListAndItem(null);
        }
    }

    private void Category_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is RadioButton rb)
            {
                rb.IsChecked = true;
                e.Handled = true;
                FocusListAndItem(null);
            }
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            FocusListAndItem(null);
        }
    }

    private void ListRadar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    private void ListRadar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is RadarViewModel vm && ListRadar.SelectedItem is RadarItem item && !ReferenceEquals(vm.SelectedItem, item))
        {
            vm.SelectedItem = item;
        }
    }

    private void ListRadar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RadarViewModel vm) return;

        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

        // Límites de navegación vertical: evitar que las flechas arriba/abajo escapen fuera de la lista
        if (!hasNavigationModifier && e.Key == Key.Up && ListRadar.SelectedIndex <= 0)
        {
            e.Handled = true;
            return;
        }

        if (!hasNavigationModifier && e.Key == Key.Down && ListRadar.SelectedIndex >= ListRadar.Items.Count - 1)
        {
            e.Handled = true;
            return;
        }

        if (!hasNavigationModifier && e.Key == Key.Right)
        {
            // Flecha Derecha en lista: abre y carga el contenido de la lista.
            // Fuera de una lista, se consume para evitar que el foco salga de la lista.
            e.Handled = true;
            if (vm.SelectedItem?.IsPlaylist == true)
            {
                vm.OpenPlaylistCommand.Execute(vm.SelectedItem);
            }
            return;
        }

        if (!hasNavigationModifier && (e.Key == Key.Left || e.Key == Key.Back))
        {
            // Flecha Izquierda o Backspace: vuelve a la vista de listas si se está dentro de una.
            // En la vista principal se consume para mantener el foco en la lista.
            e.Handled = true;
            if (vm.IsViewingPlaylistDetails)
            {
                vm.BackToPlaylistsCommand.Execute(null);
            }
            return;
        }

        if (e.Key == Key.Enter)
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
            return;
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
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
                 (modifiers & ModifierKeys.Control) != 0 &&
                 (modifiers & ModifierKeys.Shift) == 0)
        {
            if (vm.SelectedItem != null)
            {
                e.Handled = true;
                vm.SendToDownloadCommand.Execute(vm.SelectedItem);
            }
        }
    }
}
