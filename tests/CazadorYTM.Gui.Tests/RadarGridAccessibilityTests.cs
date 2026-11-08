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
    public void RadarView_GridRadar_HasRowFocusAndCellFocusDisabled()
    {
        Exception? error = null;
        DataGridSelectionUnit selectionUnit = default;
        bool directionalCycle = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var view = new RadarView();
                var gridField = typeof(RadarView).GetField("GridRadar", BindingFlags.Instance | BindingFlags.NonPublic);
                var grid = (DataGrid?)gridField?.GetValue(view) ?? throw new InvalidOperationException("GridRadar no encontrado");

                selectionUnit = grid.SelectionUnit;
                directionalCycle = KeyboardNavigation.GetDirectionalNavigation(grid) == KeyboardNavigationMode.Cycle;

                Assert.NotNull(grid.CellStyle);
                Assert.NotNull(grid.RowStyle);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.Equal(DataGridSelectionUnit.FullRow, selectionUnit);
        Assert.True(directionalCycle, "GridRadar debe tener DirectionalNavigation='Cycle'");
    }

    [Fact]
    public void RadarView_GridRadar_ArrowsNavigateRows()
    {
        Exception? error = null;
        int initialIndex = -1;
        int afterDownIndex = -1;
        int afterUpIndex = -1;

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

                var gridField = typeof(RadarView).GetField("GridRadar", BindingFlags.Instance | BindingFlags.NonPublic);
                var grid = (DataGrid?)gridField?.GetValue(view) ?? throw new InvalidOperationException("GridRadar no encontrado");

                grid.ItemsSource = vm.FilteredItems;
                grid.SelectedIndex = 0;
                initialIndex = grid.SelectedIndex;

                // Simular Key.Down en GridRadar_PreviewKeyDown
                var previewKeyDownMethod = typeof(RadarView).GetMethod("GridRadar_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
                
                var keyDownArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Down)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };

                previewKeyDownMethod?.Invoke(view, new object[] { grid, keyDownArgs });
                afterDownIndex = grid.SelectedIndex;

                var keyUpArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Up)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };

                previewKeyDownMethod?.Invoke(view, new object[] { grid, keyUpArgs });
                afterUpIndex = grid.SelectedIndex;
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
    public void RadarView_GridRadar_ArrowKeysAtEdgesRemainHandled()
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

                var gridField = typeof(RadarView).GetField("GridRadar", BindingFlags.Instance | BindingFlags.NonPublic);
                var grid = (DataGrid?)gridField?.GetValue(view) ?? throw new InvalidOperationException("GridRadar no encontrado");
                grid.ItemsSource = vm.FilteredItems;

                var previewKeyDownMethod = typeof(RadarView).GetMethod("GridRadar_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                grid.SelectedIndex = 0;
                vm.SelectedItem = items[0];
                var upArgs = CreatePreviewKeyDownArgs(Key.Up);
                previewKeyDownMethod?.Invoke(view, new object[] { grid, upArgs });
                upHandled = upArgs.Handled;
                afterUpIndex = grid.SelectedIndex;

                grid.SelectedIndex = items.Count - 1;
                vm.SelectedItem = items[^1];
                var downArgs = CreatePreviewKeyDownArgs(Key.Down);
                previewKeyDownMethod?.Invoke(view, new object[] { grid, downArgs });
                downHandled = downArgs.Handled;
                afterDownIndex = grid.SelectedIndex;

                grid.SelectedIndex = 0;
                vm.SelectedItem = items[0];
                var rightArgs = CreatePreviewKeyDownArgs(Key.Right);
                previewKeyDownMethod?.Invoke(view, new object[] { grid, rightArgs });
                rightHandled = rightArgs.Handled;
                afterRightIndex = grid.SelectedIndex;

                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(upHandled, "Up en la primera fila debe quedar manejada para no perder el foco.");
        Assert.Equal(0, afterUpIndex);
        Assert.True(downHandled, "Down en la última fila debe quedar manejada para no perder el foco.");
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
    public void HistoryView_GridHistory_ArrowsNavigateRowsAndHasDeleteAndF5Bindings()
    {
        Exception? error = null;
        int afterDownIndex = -1;
        bool hasDeleteBinding = false;
        bool hasF5Binding = false;

        WpfTestHost.Run(() =>
        {
            try
            {
                var vm = new HistoryViewModel();
                var view = new HistoryView { DataContext = vm };

                vm.FilteredEntries.Clear();
                vm.FilteredEntries.Add(new CazadorYTM.Core.Models.DownloadEntry { Title = "Track A", Url = "urlA" });
                vm.FilteredEntries.Add(new CazadorYTM.Core.Models.DownloadEntry { Title = "Track B", Url = "urlB" });

                var gridField = typeof(HistoryView).GetField("GridHistory", BindingFlags.Instance | BindingFlags.NonPublic);
                var grid = (DataGrid?)gridField?.GetValue(view) ?? throw new InvalidOperationException("GridHistory no encontrado");

                foreach (InputBinding binding in grid.InputBindings)
                {
                    if (binding is KeyBinding kb)
                    {
                        if (kb.Key == Key.Delete) hasDeleteBinding = true;
                        if (kb.Key == Key.F5) hasF5Binding = true;
                    }
                }

                grid.ItemsSource = vm.FilteredEntries;
                grid.SelectedIndex = 0;

                var previewKeyDownMethod = typeof(HistoryView).GetMethod("GridHistory_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
                var keyDownArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Down)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };

                previewKeyDownMethod?.Invoke(view, new object[] { grid, keyDownArgs });
                afterDownIndex = grid.SelectedIndex;
                vm.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.True(hasDeleteBinding, "GridHistory debe tener KeyBinding para Delete");
        Assert.True(hasF5Binding, "GridHistory debe tener KeyBinding para F5");
        Assert.Equal(1, afterDownIndex);
    }

    [Fact]
    public void HistoryView_GridHistory_HorizontalArrowsAreHandledToPreventFocusEscape()
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
                var gridField = typeof(HistoryView).GetField("GridHistory", BindingFlags.Instance | BindingFlags.NonPublic);
                var grid = (DataGrid?)gridField?.GetValue(view) ?? throw new InvalidOperationException("GridHistory no encontrado");

                var previewKeyDownMethod = typeof(HistoryView).GetMethod("GridHistory_PreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);

                var leftArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Left)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                previewKeyDownMethod?.Invoke(view, new object[] { grid, leftArgs });
                leftHandled = leftArgs.Handled;

                var rightArgs = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
                    0,
                    Key.Right)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                previewKeyDownMethod?.Invoke(view, new object[] { grid, rightArgs });
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
