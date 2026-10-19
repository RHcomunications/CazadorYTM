namespace CazadorYTM.Gui.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using CazadorYTM.Gui.ViewModels;
using CazadorYTM.Gui.Views;
using Xunit;

[Collection("WPF")]
public class MainWindowTabAccessibilityTests
{
    [Fact]
    public void MainTabs_AnnounceTabHeader_NotTheViewModelTypeName()
    {
        var peerNames = new List<string>();
        var headers = new List<string>();
        Exception? error = null;

        WpfTestHost.Run(() =>
        {
            try
            {
                var viewModel = new MainViewModel();
                var window = new MainWindow { DataContext = viewModel };
                viewModel.OpenOrFocusTab(MainTabType.History);
                viewModel.OpenOrFocusTab(MainTabType.Player);
                viewModel.OpenOrFocusTab(MainTabType.Radar);

                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1080, 780));
                root.Arrange(new Rect(0, 0, 1080, 780));
                root.UpdateLayout();

                var tabControl = FindChild<TabControl>(root)
                    ?? throw new InvalidOperationException("MainWindow no contiene un TabControl.");
                var tabItems = FindAll<TabItem>(tabControl);

                headers.AddRange(viewModel.OpenTabs.Select(tab => tab.Header));
                peerNames.AddRange(tabItems.Select(item =>
                    UIElementAutomationPeer.CreatePeerForElement(item)?.GetName() ?? string.Empty));
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
        Assert.Equal(4, headers.Count);
        Assert.True(headers.SequenceEqual(peerNames),
            "El nombre accesible de las pestañas debe ser su encabezado.\n" +
            "encabezados=" + string.Join("|", headers) + "\n" +
            "anunciado=" + string.Join("|", peerNames));
        Assert.All(peerNames, name => Assert.DoesNotContain("MainTabItem", name));
        Assert.All(peerNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) return typed;
            var found = FindChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var list = new List<T>();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) list.Add(typed);
            list.AddRange(FindAll<T>(child));
        }
        return list;
    }
}
