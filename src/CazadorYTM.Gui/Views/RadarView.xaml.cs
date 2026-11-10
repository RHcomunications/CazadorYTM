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
            if (target == null) return;

            GridRadar.SelectedItem = target;
            GridRadar.ScrollIntoView(target);
            FocusRow(GridRadar.Items.IndexOf(target));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FocusRow(int index)
    {
        if (index < 0 || index >= GridRadar.Items.Count) return;

        var item = GridRadar.Items[index];
        GridRadar.ScrollIntoView(item);

        if (GridRadar.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
        {
            row.Focus();
            Keyboard.Focus(row);
        }
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

    private void GridRadar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is RadarViewModel vm && GridRadar.SelectedItem is RadarItem item && !ReferenceEquals(vm.SelectedItem, item))
        {
            vm.SelectedItem = item;
        }
    }

    private void GridRadar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RadarViewModel vm) return;

        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

        if (!hasNavigationModifier && e.Key is Key.Up or Key.Down)
        {
            // La cuadrícula debe conservar el foco incluso en el primer/último elemento.
            // Si no se marca la tecla como manejada, WPF puede mover el foco a la pestaña.
            e.Handled = true;

            var count = GridRadar.Items.Count;
            if (count == 0) return;

            var currentIndex = GridRadar.SelectedIndex;
            var nextIndex = e.Key == Key.Down
                ? (currentIndex < 0 ? 0 : System.Math.Min(currentIndex + 1, count - 1))
                : (currentIndex <= 0 ? 0 : currentIndex - 1);

            if (nextIndex != currentIndex || currentIndex < 0)
            {
                GridRadar.SelectedIndex = nextIndex;
                if (GridRadar.Items[nextIndex] is RadarItem nextItem)
                {
                    vm.SelectedItem = nextItem;
                }
            }

            FocusRow(nextIndex);
            return;
        }

        if (!hasNavigationModifier && e.Key == Key.Right)
        {
            // Flecha Derecha en lista: abre y carga el contenido de la lista.
            // Fuera de una lista, se consume para evitar que el foco salga de la cuadrícula.
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
            // En la vista principal se consume para mantener el foco en la cuadrícula.
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
        }
        else if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
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
