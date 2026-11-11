namespace CazadorYTM.Gui.Tests;

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Core.Services;
using CazadorYTM.Gui.ViewModels;
using CazadorYTM.Gui.Views;
using Xunit;

[Collection("WPF")]
public class RadarGridAccessibilityTests
{
    [Fact]
    public void RadarItem_DisplayText_ReturnsFormattedString()
    {
        var itemWithArtist = new RadarItem
        {
            Id = "abc",
            Title = "Canción de Prueba",
            Artist = "Artista Famoso",
            Duration = 180
        };

        var itemWithoutArtist = new RadarItem
        {
            Id = "def",
            Title = "Sin Artista",
            Duration = 120
        };

        Assert.Equal("Canción de Prueba — Artista Famoso", itemWithArtist.DisplayText);
        Assert.Equal("Canción de Prueba — Artista Famoso", itemWithArtist.ToString());
        Assert.Equal("Sin Artista", itemWithoutArtist.DisplayText);
    }

    [Fact]
    public void RadarView_ListRadar_HasContainedNavigationAndAccessibleItemStyle()
    {
        Exception? error = null;
        bool directionalContained = false;
        bool isSingleSelection = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var view = new RadarView();
                var listField = typeof(RadarView).GetField("ListRadar", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = (ListBox?)listField?.GetValue(view) ?? throw new InvalidOperationException("ListRadar no encontrado");

                isSingleSelection = list.SelectionMode == SelectionMode.Single;
                directionalContained = KeyboardNavigation.GetDirectionalNavigation(list) == KeyboardNavigationMode.Contained;

                Assert.NotNull(list.ItemContainerStyle);
                Assert.NotNull(list.ItemTemplate);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(isSingleSelection, "ListRadar debe tener SelectionMode='Single'");
        Assert.True(directionalContained, "ListRadar debe tener DirectionalNavigation='Contained' para evitar que el foco escape a la pestaña");
    }

    [Fact]
    public void RadarView_ListRadar_ArrowsNavigateItems()
    {
        Exception? error = null;
        int initialIndex = -1;
        bool rightHandled = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new RadarViewModel();
                var view = new RadarView { DataContext = vm };

                var items = new ObservableCollection<RadarItem>
                {
                    new() { Id = "1", Title = "Pista 1", Artist = "Artista 1" },
                    new() { Id = "2", Title = "Pista 2", Artist = "Artista 2" },
                    new() { Id = "3", Title = "Pista 3", Artist = "Artista 3" }
                };

                vm.FilteredItems.Clear();
                foreach (var it in items)
                {
                    vm.FilteredItems.Add(it);
                }

                var listField = typeof(RadarView).GetField("ListRadar", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = (ListBox?)listField?.GetValue(view) ?? throw new InvalidOperationException("ListRadar no encontrado");

                list.ItemsSource = vm.FilteredItems;
                list.SelectedIndex = 0;
                initialIndex = list.SelectedIndex;

                var previewKeyDownMethod = typeof(RadarView).GetMethod("ListRadar_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                var rightArgs = CreatePreviewKeyDownArgs(Key.Right);
                previewKeyDownMethod?.Invoke(view, new object[] { list, rightArgs });
                rightHandled = rightArgs.Handled;
                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.Equal(0, initialIndex);
        Assert.True(rightHandled, "Right fuera de playlist debe ser manejado para evitar pérdida de foco.");
    }

    [Fact]
    public void RadarView_FilterToRadarList_DownAndEnterHandled()
    {
        Exception? error = null;
        bool downHandled = false;
        bool enterHandled = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new RadarViewModel();
                var view = new RadarView { DataContext = vm };

                var filterField = typeof(RadarView).GetField("TxtRadarFilter", BindingFlags.Instance | BindingFlags.NonPublic);
                var filter = (TextBox?)filterField?.GetValue(view) ?? throw new InvalidOperationException("TxtRadarFilter no encontrado");

                var filterPreviewKeyDown = typeof(RadarView).GetMethod("TxtRadarFilter_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                var downArgs = CreatePreviewKeyDownArgs(Key.Down);
                filterPreviewKeyDown?.Invoke(view, new object[] { filter, downArgs });
                downHandled = downArgs.Handled;

                var enterArgs = CreatePreviewKeyDownArgs(Key.Enter);
                filterPreviewKeyDown?.Invoke(view, new object[] { filter, enterArgs });
                enterHandled = enterArgs.Handled;

                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(downHandled, "Down en el filtro debe quedar manejado para transferir foco a la lista.");
        Assert.True(enterHandled, "Enter en el filtro debe quedar manejado para transferir foco a la lista.");
    }

    [Fact]
    public void RadarView_ListRadar_ArrowKeysAtEdgesRemainHandled()
    {
        Exception? error = null;
        bool upHandled = false;
        bool downHandled = false;
        bool rightHandled = false;
        int afterUpIndex = -1;
        int afterDownIndex = -1;
        int afterRightIndex = -1;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new RadarViewModel();
                var view = new RadarView { DataContext = vm };

                var items = new ObservableCollection<RadarItem>
                {
                    new() { Id = "1", Title = "Pista 1", Artist = "Artista 1" },
                    new() { Id = "2", Title = "Pista 2", Artist = "Artista 2" },
                    new() { Id = "3", Title = "Pista 3", Artist = "Artista 3" }
                };

                vm.FilteredItems.Clear();
                foreach (var it in items)
                {
                    vm.FilteredItems.Add(it);
                }

                var listField = typeof(RadarView).GetField("ListRadar", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = (ListBox?)listField?.GetValue(view) ?? throw new InvalidOperationException("ListRadar no encontrado");
                list.ItemsSource = vm.FilteredItems;

                var previewKeyDownMethod = typeof(RadarView).GetMethod("ListRadar_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                list.SelectedIndex = 0;
                vm.SelectedItem = items[0];
                var upArgs = CreatePreviewKeyDownArgs(Key.Up);
                previewKeyDownMethod?.Invoke(view, new object[] { list, upArgs });
                upHandled = upArgs.Handled;
                afterUpIndex = list.SelectedIndex;

                list.SelectedIndex = items.Count - 1;
                vm.SelectedItem = items[^1];
                var downArgs = CreatePreviewKeyDownArgs(Key.Down);
                previewKeyDownMethod?.Invoke(view, new object[] { list, downArgs });
                downHandled = downArgs.Handled;
                afterDownIndex = list.SelectedIndex;

                list.SelectedIndex = 0;
                vm.SelectedItem = items[0];
                var rightArgs = CreatePreviewKeyDownArgs(Key.Right);
                previewKeyDownMethod?.Invoke(view, new object[] { list, rightArgs });
                rightHandled = rightArgs.Handled;
                afterRightIndex = list.SelectedIndex;

                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(upHandled, "Up en el primer elemento debe quedar manejada para no perder el foco.");
        Assert.Equal(0, afterUpIndex);
        Assert.True(downHandled, "Down en el último elemento debe quedar manejada para no perder el foco.");
        Assert.Equal(2, afterDownIndex);
        Assert.True(rightHandled, "Right fuera de una lista debe quedar manejada para no perder el foco.");
        Assert.Equal(0, afterRightIndex);
    }

    private static KeyEventArgs CreatePreviewKeyDownArgs(Key key)
    {
        return new KeyEventArgs(
            Keyboard.PrimaryDevice,
            new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
            0,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
    }

    [Fact]
    public void ShortcutItem_ToString_ReturnsReadableScreenReaderLabel()
    {
        var item = new ShortcutItem("Ctrl + L", "Abrir tabla de atajos");
        Assert.Equal("Ctrl + L: Abrir tabla de atajos", item.ToString());
    }

    [Fact]
    public void HistoryView_ListHistory_HasContainedNavigationAndAccessibleItemStyle()
    {
        Exception? error = null;
        bool directionalContained = false;
        bool isSingleSelection = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var view = new HistoryView();
                var listField = typeof(HistoryView).GetField("ListHistory", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = (ListBox?)listField?.GetValue(view) ?? throw new InvalidOperationException("ListHistory no encontrado");

                isSingleSelection = list.SelectionMode == SelectionMode.Single;
                directionalContained = KeyboardNavigation.GetDirectionalNavigation(list) == KeyboardNavigationMode.Contained;

                Assert.NotNull(list.ItemContainerStyle);
                Assert.NotNull(list.ItemTemplate);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(isSingleSelection, "ListHistory debe tener SelectionMode='Single'");
        Assert.True(directionalContained, "ListHistory debe tener DirectionalNavigation='Contained'");
    }

    [Fact]
    public void HistoryView_ListHistory_ArrowsNavigateItems()
    {
        Exception? error = null;
        int initialIndex = -1;
        int afterDownIndex = -1;
        int afterUpIndex = -1;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new HistoryViewModel();
                var view = new HistoryView { DataContext = vm };

                vm.FilteredEntries.Clear();
                vm.FilteredEntries.Add(new CazadorYTM.Core.Models.DownloadEntry { Title = "Track A", Url = "urlA" });
                vm.FilteredEntries.Add(new CazadorYTM.Core.Models.DownloadEntry { Title = "Track B", Url = "urlB" });

                var listField = typeof(HistoryView).GetField("ListHistory", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = (ListBox?)listField?.GetValue(view) ?? throw new InvalidOperationException("ListHistory no encontrado");

                list.ItemsSource = vm.FilteredEntries;
                list.SelectedIndex = 0;
                initialIndex = list.SelectedIndex;

                var previewKeyDownMethod = typeof(HistoryView).GetMethod("ListHistory_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
                var keyDownArgs = CreatePreviewKeyDownArgs(Key.Down);
                previewKeyDownMethod?.Invoke(view, new object[] { list, keyDownArgs });
                afterDownIndex = list.SelectedIndex;

                var keyUpArgs = CreatePreviewKeyDownArgs(Key.Up);
                previewKeyDownMethod?.Invoke(view, new object[] { list, keyUpArgs });
                afterUpIndex = list.SelectedIndex;

                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.Equal(0, initialIndex);
        Assert.Equal(1, afterDownIndex);
        Assert.Equal(0, afterUpIndex);
    }

    [Fact]
    public void HistoryView_FilterToHistoryList_DownAndEnterHandled()
    {
        Exception? error = null;
        bool downHandled = false;
        bool enterHandled = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new HistoryViewModel();
                var view = new HistoryView { DataContext = vm };

                var filterField = typeof(HistoryView).GetField("TxtSearchHistory", BindingFlags.Instance | BindingFlags.NonPublic);
                var filter = (TextBox?)filterField?.GetValue(view) ?? throw new InvalidOperationException("TxtSearchHistory no encontrado");

                var filterPreviewKeyDown = typeof(HistoryView).GetMethod("TxtSearchHistory_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                var downArgs = CreatePreviewKeyDownArgs(Key.Down);
                filterPreviewKeyDown?.Invoke(view, new object[] { filter, downArgs });
                downHandled = downArgs.Handled;

                var enterArgs = CreatePreviewKeyDownArgs(Key.Enter);
                filterPreviewKeyDown?.Invoke(view, new object[] { filter, enterArgs });
                enterHandled = enterArgs.Handled;

                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(downHandled, "Down en el buscador de biblioteca debe quedar manejado para transferir foco a la lista.");
        Assert.True(enterHandled, "Enter en el buscador de biblioteca debe quedar manejado para transferir foco a la lista.");
    }

    [Fact]
    public void HistoryView_ListHistory_HorizontalArrowsAreHandledToPreventFocusEscape()
    {
        Exception? error = null;
        bool leftHandled = false;
        bool rightHandled = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new HistoryViewModel();
                var view = new HistoryView { DataContext = vm };
                var listField = typeof(HistoryView).GetField("ListHistory", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = (ListBox?)listField?.GetValue(view) ?? throw new InvalidOperationException("ListHistory no encontrado");

                var previewKeyDownMethod = typeof(HistoryView).GetMethod("ListHistory_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                var leftArgs = CreatePreviewKeyDownArgs(Key.Left);
                previewKeyDownMethod?.Invoke(view, new object[] { list, leftArgs });
                leftHandled = leftArgs.Handled;

                var rightArgs = CreatePreviewKeyDownArgs(Key.Right);
                previewKeyDownMethod?.Invoke(view, new object[] { list, rightArgs });
                rightHandled = rightArgs.Handled;

                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(leftHandled, "Flecha izquierda debe ser manejada para evitar escape de foco");
        Assert.True(rightHandled, "Flecha derecha debe ser manejada para evitar escape de foco");
    }
}
