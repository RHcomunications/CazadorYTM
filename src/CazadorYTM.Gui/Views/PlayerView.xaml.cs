namespace CazadorYTM.Gui.Views;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CazadorYTM.Gui.ViewModels;

public partial class PlayerView : UserControl
{
    private PlayerViewModel? _subscribedVm;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    public void FocusDefaultControl()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (BtnPlayPause != null && BtnPlayPause.IsVisible && BtnPlayPause.IsEnabled)
            {
                BtnPlayPause.Focus();
                Keyboard.Focus(BtnPlayPause);
            }
            else if (SldPosition != null && SldPosition.IsVisible)
            {
                SldPosition.Focus();
                Keyboard.Focus(SldPosition);
            }
            else
            {
                Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeViewModel(DataContext as PlayerViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            SubscribeViewModel(e.NewValue as PlayerViewModel);
        }
    }

    private void SubscribeViewModel(PlayerViewModel? vm)
    {
        UnsubscribeViewModel();
        if (vm != null)
        {
            _subscribedVm = vm;
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnsubscribeViewModel()
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentLyricIndex) && DataContext is PlayerViewModel vm)
        {
            if (vm.CurrentLyricIndex >= 0 && vm.CurrentLyricIndex < vm.Lyrics.Count)
            {
                var targetItem = vm.Lyrics[vm.CurrentLyricIndex];
                Dispatcher.InvokeAsync(() =>
                {
                    if (ListLyrics != null && !ListLyrics.IsKeyboardFocusWithin)
                    {
                        ListLyrics.ScrollIntoView(targetItem);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}
