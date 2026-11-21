namespace CazadorYTM.Gui.Tests;

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using CazadorYTM.Gui.Services;
using CazadorYTM.Gui.ViewModels;
using CazadorYTM.Gui.Views;
using Xunit;

public class HardenedServicesAndPreferencesTests
{
    [Fact]
    public void AudioPlayerService_UpdateQueueWithoutRestarting_FiltersInvalidPathsAndRemapsIndex()
    {
        WpfTestHost.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"cazador_queue_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var fileA = Path.Combine(tempDir, "a.mp3");
                var fileB = Path.Combine(tempDir, "b.mp3");
                File.WriteAllBytes(fileA, new byte[] { 1, 2, 3 });
                File.WriteAllBytes(fileB, new byte[] { 4, 5, 6 });

                var player = AudioPlayerService.Instance;
                var input = new[]
                {
                    "relative_not_allowed.mp3",
                    Path.Combine(tempDir, "missing.mp3"),
                    fileA,
                    "https://example.com/stream.m4a",
                    fileB
                };

                // Original index 4 points to fileB; after filtering invalid entries it should remap to index 2.
                player.UpdateQueueWithoutRestarting(input, currentIndex: 4);

                Assert.Equal(3, player.Queue.Count);
                Assert.Equal(fileA, player.Queue[0]);
                Assert.Equal("https://example.com/stream.m4a", player.Queue[1]);
                Assert.Equal(fileB, player.Queue[2]);
                Assert.Equal(2, player.CurrentQueueIndex);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });
    }

    [Fact]
    public void LogService_GetAllLogsText_FlushesAndReturnsWrittenMarker()
    {
        var marker = $"DIAG_MARKER_{Guid.NewGuid():N}";
        LogService.Instance.Info(marker);

        var allLogs = LogService.Instance.GetAllLogsText();
        Assert.Contains(marker, allLogs);
    }

    [Fact]
    public void MainViewModel_UpdateTaskbarProgress_DrivenByTypedPhaseRegardlessOfLanguage()
    {
        WpfTestHost.Run(() =>
        {
            using var mainVm = new MainViewModel();

            mainVm.DownloadVM.IsBusy = true;
            mainVm.DownloadVM.StatusText = "English status text with no Spanish keywords";

            mainVm.DownloadVM.CurrentPhase = DownloadTaskPhase.Analyzing;
            mainVm.DownloadVM.ProgressPercentage = 0;
            Assert.Equal(TaskbarItemProgressState.Indeterminate, mainVm.TaskbarProgressState);

            mainVm.DownloadVM.CurrentPhase = DownloadTaskPhase.Downloading;
            mainVm.DownloadVM.ProgressPercentage = 50;
            Assert.Equal(TaskbarItemProgressState.Normal, mainVm.TaskbarProgressState);
            Assert.Equal(0.5, mainVm.TaskbarProgressValue, precision: 2);

            mainVm.DownloadVM.CurrentPhase = DownloadTaskPhase.PostProcessing;
            mainVm.DownloadVM.ProgressPercentage = 100;
            Assert.Equal(TaskbarItemProgressState.Indeterminate, mainVm.TaskbarProgressState);

            mainVm.DownloadVM.CurrentPhase = DownloadTaskPhase.Error;
            Assert.Equal(TaskbarItemProgressState.Error, mainVm.TaskbarProgressState);

            mainVm.DownloadVM.IsBusy = false;
            Assert.Equal(TaskbarItemProgressState.None, mainVm.TaskbarProgressState);
            Assert.Equal(0.0, mainVm.TaskbarProgressValue);
        });
    }

    [Fact]
    public void PreferencesWindow_ExposesFourTabsCycleNavigationAndDiagnosticsCommand()
    {
        WpfTestHost.Run(() =>
        {
            var win = new PreferencesWindow();
            try
            {
                var prefTabs = win.FindName("PrefTabs") as TabControl;
                var btnCancel = win.FindName("BtnCancel") as Button;
                var vm = win.DataContext as PreferencesViewModel;

                Assert.NotNull(prefTabs);
                Assert.NotNull(btnCancel);
                Assert.NotNull(vm);
                Assert.Equal(4, prefTabs!.Items.Count);
                Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(win));
                Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(prefTabs));
                Assert.NotNull(vm!.LogsVM.ExportDiagnosticsCommand);
                Assert.Equal(4, vm.StartupTabOptions.Count);
            }
            finally
            {
                if (win.DataContext is IDisposable d)
                {
                    d.Dispose();
                }
            }
        });
    }
}
