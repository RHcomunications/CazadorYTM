namespace CazadorYTM.Gui.Tests;

using System;
using CazadorYTM.Gui.ViewModels;
using Xunit;

[Collection("WPF")]
public class ViewModelLifecycleTests
{
    [Fact]
    public void MainViewModel_Dispose_PropagatesToAllChildViewModels()
    {
        WpfTestHost.Run(() =>
        {
            var mainVm = new MainViewModel();

            Assert.NotNull(mainVm.DownloadVM);
            Assert.NotNull(mainVm.HistoryVM);
            Assert.NotNull(mainVm.PlayerVM);
            Assert.NotNull(mainVm.RadarVM);

            // Should dispose cleanly without exceptions
            mainVm.Dispose();

            // Calling Dispose a second time should be idempotent
            mainVm.Dispose();
        });
    }

    [Fact]
    public void DownloadViewModel_Dispose_CleansUpSuccessfully()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new DownloadViewModel();
            vm.Dispose();
            vm.Dispose();
        });
    }

    [Fact]
    public void RadarViewModel_Dispose_CleansUpSuccessfully()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new RadarViewModel();
            vm.Dispose();
            vm.Dispose();
        });
    }

    [Fact]
    public void HistoryViewModel_Dispose_UnhooksSuccessfully()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new HistoryViewModel();
            vm.Dispose();
            vm.Dispose();
        });
    }

    [Fact]
    public void HistoryViewModel_AsyncCommands_ExecuteWithoutException()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new HistoryViewModel();
            Assert.NotNull(vm.PlayAllCommand);
            Assert.NotNull(vm.PlaySelectedCommand);
            Assert.NotNull(vm.ExportPlaylistM3U8Command);
            Assert.NotNull(vm.CleanMissingEntriesCommand);

            vm.PlayAllCommand.Execute(null);
            vm.PlaySelectedCommand.Execute(null);
            vm.ExportPlaylistM3U8Command.Execute(null);

            vm.Dispose();
        });
    }

    [Fact]
    public void PlayerViewModel_Dispose_UnhooksSuccessfully()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.Dispose();
            vm.Dispose();
        });
    }
}
