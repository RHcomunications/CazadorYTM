namespace CazadorYTM.Gui.Views;

using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using CazadorYTM.Gui.ViewModels;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
    }

    private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            var pct = (int)Math.Round(e.NewValue * 100);
            var text = pct == 0 ? "Silenciado" : $"Volumen {pct} por ciento";
            vm.VolumeAnnouncement = text;
            vm.AccessibleVolumeText = text;

            try
            {
                VolumeLiveAnnouncer.Text = text;
                var peer = UIElementAutomationPeer.FromElement(VolumeLiveAnnouncer) 
                           ?? UIElementAutomationPeer.CreatePeerForElement(VolumeLiveAnnouncer);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            catch { }
        }
    }
}
