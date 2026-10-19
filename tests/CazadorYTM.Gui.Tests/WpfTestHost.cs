namespace CazadorYTM.Gui.Tests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

internal static class WpfTestHost
{
    private static Dispatcher? _dispatcher;

    public static void Run(Action action)
    {
        EnsureStarted();
        _dispatcher!.Invoke(action);
    }

    private static void EnsureStarted()
    {
        if (_dispatcher is not null) return;

        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            var application = Application.Current as CazadorYTM.Gui.App;
            if (application is null)
            {
                application = new CazadorYTM.Gui.App();
                application.InitializeComponent();
            }

            _dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WpfTestHost"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
    }
}
