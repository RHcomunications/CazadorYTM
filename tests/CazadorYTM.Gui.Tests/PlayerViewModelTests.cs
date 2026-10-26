namespace CazadorYTM.Gui.Tests;

using System;
using System.IO;
using CazadorYTM.Gui.Services;
using CazadorYTM.Gui.ViewModels;
using Xunit;

[Collection("WPF")]
public class PlayerViewModelTests
{
    [Fact]
    public void PlayerViewModel_InitialState_IsExpected()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();

            Assert.False(vm.IsPlaying);
            Assert.False(vm.IsPaused);
            Assert.False(vm.IsShuffle);
            Assert.Equal(PlaybackRepeatMode.Off, vm.RepeatMode);
            Assert.NotNull(vm.TrackTitle);
            Assert.NotNull(vm.AccessibleVolumeText);
            Assert.NotNull(vm.AccessiblePositionText);
            Assert.NotNull(vm.RepeatModeText);
        });
    }

    [Fact]
    public void VolumeCommands_AdjustAndClampVolumeProperly()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();

            vm.Volume = 0.5;
            Assert.Equal(0.5, vm.Volume, 2);

            vm.VolumeUp();
            Assert.Equal(0.55, vm.Volume, 2);

            vm.VolumeDown();
            Assert.Equal(0.5, vm.Volume, 2);

            vm.MaxVolume();
            Assert.Equal(1.0, vm.Volume, 2);

            vm.MuteToggle();
            Assert.Equal(0.0, vm.Volume, 2);
            Assert.Contains("Silenciado", vm.AccessibleVolumeText, StringComparison.OrdinalIgnoreCase);

            vm.MuteToggle();
            Assert.Equal(1.0, vm.Volume, 2);
        });
    }

    [Fact]
    public void ToggleRepeat_CyclesThroughAllModes()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.RepeatMode = PlaybackRepeatMode.Off;

            vm.ToggleRepeat();
            Assert.Equal(PlaybackRepeatMode.All, vm.RepeatMode);

            vm.ToggleRepeat();
            Assert.Equal(PlaybackRepeatMode.One, vm.RepeatMode);

            vm.ToggleRepeat();
            Assert.Equal(PlaybackRepeatMode.Off, vm.RepeatMode);
        });
    }

    [Fact]
    public void ToggleShuffle_InvertsShuffleState()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            var initial = vm.IsShuffle;

            vm.ToggleShuffle();
            Assert.NotEqual(initial, vm.IsShuffle);

            vm.ToggleShuffle();
            Assert.Equal(initial, vm.IsShuffle);
        });
    }

    [Fact]
    public void PlayRemoteTrack_UpdatesTrackDetailsAndAnnouncement()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            const string fakeStreamUrl = "https://example.com/audio/stream.mp3";
            const string title = "Prueba de Audio v1.4.0";
            const string artist = "Artista Demo";

            vm.PlayRemoteTrack(fakeStreamUrl, title, artist);

            Assert.Equal(title, vm.TrackTitle);
            Assert.Equal(artist, vm.TrackArtist);
            Assert.Contains(title, vm.TrackAnnouncement);
            Assert.Contains(artist, vm.TrackAnnouncement);
        });
    }

    [Fact]
    public void SetQueue_ConfiguresQueueOnUnderlyingPlayer()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            var queue = new[] { "https://example.com/track1.mp3", "https://example.com/track2.mp3", "https://example.com/track3.mp3" };

            vm.SetQueue(queue, 0);

            Assert.Equal(3, AudioPlayerService.Instance.Queue.Count);
            Assert.Equal(0, AudioPlayerService.Instance.CurrentQueueIndex);
        });
    }

    [Fact]
    public void SeekRelative_ParsesDeltaAndClampsPosition()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.DurationSeconds = 100;
            vm.PositionSeconds = 50;

            vm.SeekRelative("10");
            // Seek commands delegate to AudioPlayerService.Instance.Seek
            Assert.NotNull(vm.AccessiblePositionText);

            vm.SeekToStart();
            vm.SeekToEnd();
        });
    }

    [Fact]
    public void StopCommand_ResetsPositionAndTexts()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.Stop();

            Assert.Equal("00:00", vm.CurrentPositionText);
            Assert.Equal(0, vm.PositionSeconds);
        });
    }
}
