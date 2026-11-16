namespace CazadorYTM.Gui.Accessibility;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// Shared keyboard-navigation and deterministic item-focus helper for virtualized ListBox views.
/// </summary>
public static class ListBoxNavigationHelper
{
    public static void FocusListAndItem(ListBox listBox, object? item = null)
    {
        listBox.Dispatcher.InvokeAsync(() =>
        {
            if (listBox.Visibility != Visibility.Visible) return;

            var target = item ?? listBox.SelectedItem ?? (listBox.Items.Count > 0 ? listBox.Items[0] : null);
            if (target == null)
            {
                listBox.Focus();
                return;
            }

            var index = listBox.Items.IndexOf(target);
            if (index < 0) index = 0;

            listBox.SelectedIndex = index;
            FocusItemContainer(listBox, index);
        }, DispatcherPriority.Loaded);
    }

    public static void FocusItemContainer(ListBox listBox, int index)
    {
        if (index < 0 || index >= listBox.Items.Count) return;

        var item = listBox.Items[index];
        listBox.ScrollIntoView(item);

        if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem lbi)
        {
            lbi.Focus();
            Keyboard.Focus(lbi);
        }
        else
        {
            listBox.Dispatcher.InvokeAsync(() =>
            {
                if (index < listBox.Items.Count &&
                    listBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem retryLbi)
                {
                    retryLbi.Focus();
                    Keyboard.Focus(retryLbi);
                }
                else
                {
                    listBox.Focus();
                }
            }, DispatcherPriority.Loaded);
        }
    }

    public static void SyncSelectionFocus(ListBox listBox)
    {
        if (listBox.SelectedIndex >= 0 && listBox.IsKeyboardFocusWithin)
        {
            FocusItemContainer(listBox, listBox.SelectedIndex);
        }
    }

    public static bool HandleUpDownKey(ListBox listBox, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var hasNavigationModifier =
            (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0;

        if (hasNavigationModifier || e.Key is not (Key.Up or Key.Down))
        {
            return false;
        }

        e.Handled = true;
        var count = listBox.Items.Count;
        if (count == 0) return true;

        var curIdx = listBox.SelectedIndex;
        if (curIdx < 0)
        {
            listBox.SelectedIndex = 0;
            FocusItemContainer(listBox, 0);
            return true;
        }

        if (e.Key == Key.Up)
        {
            var nextIdx = curIdx == 0 ? 0 : curIdx - 1;
            if (nextIdx != curIdx) listBox.SelectedIndex = nextIdx;
            FocusItemContainer(listBox, nextIdx);
            return true;
        }

        var downIdx = curIdx >= count - 1 ? count - 1 : curIdx + 1;
        if (downIdx != curIdx) listBox.SelectedIndex = downIdx;
        FocusItemContainer(listBox, downIdx);
        return true;
    }
}
