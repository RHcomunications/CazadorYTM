namespace CazadorYTM.Gui.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Xunit;

public class ViewSmokeTests
{
    [Fact]
    public void EveryView_LoadsWithoutThrowing()
    {
        var failures = new List<string>();
        var loadedCount = 0;
        Exception? startupError = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new CazadorYTM.Gui.App();
                    app.InitializeComponent();
                }

                var viewTypes = typeof(CazadorYTM.Gui.App).Assembly.GetTypes()
                    .Where(t => !t.IsAbstract && t.IsPublic)
                    .Where(t => typeof(Window).IsAssignableFrom(t) || typeof(UserControl).IsAssignableFrom(t))
                    .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
                    .OrderBy(t => t.FullName)
                    .ToList();

                loadedCount = viewTypes.Count;

                foreach (var type in viewTypes)
                {
                    try
                    {
                        _ = Activator.CreateInstance(type);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException is not null)
                    {
                        failures.Add($"{type.Name}: {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{type.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                startupError = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(startupError);
        Assert.True(loadedCount >= 10, $"La detección de vistas por reflexión encontró solo {loadedCount} vistas.");
        Assert.True(failures.Count == 0,
            "Una o más vistas fallaron al cargar su XAML:\n" + string.Join("\n", failures));
    }
}
