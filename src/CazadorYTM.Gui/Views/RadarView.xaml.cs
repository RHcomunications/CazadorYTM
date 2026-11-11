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

    public void FocusListAndItem(RadarItem? item = null)
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

            var index = ListRadar.Items.IndexOf(target);
            if (index < 0) index = 0;

            ListRadar.SelectedIndex = index;
            FocusItemContainer(index);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FocusItemContainer(int index)
    {
        if (index < 0 || index >= ListRadar.Items.Count) return;

        var item = ListRadar.Items[index];
        ListRadar.ScrollIntoView(item);

        if (ListRadar.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem lbi)
        {
            lbi.Focus();
            Keyboard.Focus(lbi);
        }
        else
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (index < ListRadar.Items.Count &&
                    ListRadar.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem retryLbi)
                {
                    retryLbi.Focus();
                    Keyboard.Focus(retryLbi);
                }
                else
                {
                    ListRadar.Focus();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
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

        if (ListRadar.SelectedIndex >= 0 && ListRadar.IsKeyboardFocusWithin)
        {
            FocusItemContainer(ListRadar.SelectedIndex);
        }
    }

    private void ListRadar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RadarViewModel vm) return;

        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

        if (!hasNavigationModifier && e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;

            var count = ListRadar.Items.Count;
            if (count == 0) return;

            var curIdx = ListRadar.SelectedIndex;
            if (curIdx < 0)
            {
                ListRadar.SelectedIndex = 0;
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
                ListRadar.SelectedIndex = nextIdx;
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
                ListRadar.SelectedIndex = nextIdx;
                FocusItemContainer(nextIdx);
                return;
            }
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
