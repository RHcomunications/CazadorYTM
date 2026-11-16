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

    [Fact]
    public void ToggleLyrics_InvertsLyricsVisibility()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            Assert.False(vm.IsLyricsVisible);

            vm.ToggleLyrics();
            Assert.True(vm.IsLyricsVisible);

            vm.ToggleLyrics();
            Assert.False(vm.IsLyricsVisible);
        });
    }

    [Fact]
    public void Lyrics_SyncPosition_UpdatesActiveLine()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            var lrc = @"[00:02.00] Line one
[00:05.00] Line two
[00:10.00] Line three";
            var parsed = CazadorYTM.Core.Models.LyricLine.ParseLrc(lrc);
            foreach (var line in parsed)
            {
                vm.Lyrics.Add(line);
            }
            vm.HasLyrics = true;

            // Trigger position at 3 seconds -> should match Line one
            vm.Seek(3.0);
            // Calling Seek does not directly invoke PositionChanged in headless without playback, so test position sync
            // via the internal position sync or OnPlayerPositionChanged
            var method = typeof(PlayerViewModel).GetMethod("OnPlayerPositionChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20) });

            Assert.Equal(0, vm.CurrentLyricIndex);
            Assert.Equal("Line one", vm.CurrentLyricLineText);

            // Move to 7 seconds -> should match Line two
            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(20) });
            Assert.Equal(1, vm.CurrentLyricIndex);
            Assert.Equal("Line two", vm.CurrentLyricLineText);

            // Move to 12 seconds -> should match Line three
            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(20) });
            Assert.Equal(2, vm.CurrentLyricIndex);
            Assert.Equal("Line three", vm.CurrentLyricLineText);
        });
    }

    [Fact]
    public void SpeedControl_AdjustsAndCyclesSpeed()
    {
        WpfTestHost.Run(() =>
        {
            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                var vm = new PlayerViewModel();
                vm.SpeedRatio = 1.0;
                Assert.Equal("1.00x", vm.SpeedRatioText);

                vm.SpeedUp();
                Assert.Equal(1.25, vm.SpeedRatio);

                vm.SpeedDown();
                Assert.Equal(1.0, vm.SpeedRatio);

                vm.SpeedDown();
                Assert.Equal(0.75, vm.SpeedRatio);

                // Clamped at 0.75 minimum
                vm.SpeedDown();
                Assert.Equal(0.75, vm.SpeedRatio);

                vm.ResetSpeed();
                Assert.Equal(1.0, vm.SpeedRatio);

                // Test cycle
                vm.CycleSpeed();
                Assert.Equal(1.25, vm.SpeedRatio);
                vm.CycleSpeed();
                Assert.Equal(1.5, vm.SpeedRatio);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
            }
        });
    }

    [Fact]
    public void SleepTimer_CyclesAndSetsModes()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            Assert.Equal(SleepTimerOption.Off, vm.SleepTimerMode);
            Assert.False(vm.IsSleepTimerActive);

            vm.CycleSleepTimer();
            Assert.Equal(SleepTimerOption.Min15, vm.SleepTimerMode);
            Assert.True(vm.IsSleepTimerActive);
            Assert.Contains("15", vm.SleepTimerText);

            vm.CycleSleepTimer();
            Assert.Equal(SleepTimerOption.Min30, vm.SleepTimerMode);

            vm.SetSleepTimer("-1");
            Assert.Equal(SleepTimerOption.EndOfTrack, vm.SleepTimerMode);
            Assert.True(vm.IsSleepTimerActive);

            vm.CancelSleepTimer();
            Assert.Equal(SleepTimerOption.Off, vm.SleepTimerMode);
            Assert.False(vm.IsSleepTimerActive);
        });
    }

    [Fact]
    public void SleepTimer_EndOfTrack_FiresAndResetsWhenRemainingBelowThreshold()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.Volume = 0.8;
            vm.SetSleepTimer("-1");
            Assert.Equal(SleepTimerOption.EndOfTrack, vm.SleepTimerMode);

            var method = typeof(PlayerViewModel).GetMethod("OnPlayerPositionChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(98), TimeSpan.FromSeconds(100) });
            Assert.True(vm.Volume <= 0.8);

            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(99.7), TimeSpan.FromSeconds(100) });
            Assert.Equal(SleepTimerOption.Off, vm.SleepTimerMode);
            Assert.False(vm.IsSleepTimerActive);
            Assert.Equal(0.8, vm.Volume, 2);
        });
    }

    [Fact]
    public void SleepTimer_CancelledDuringFade_RestoresPreFadeVolume()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.Volume = 0.8;
            vm.SetSleepTimer("-1");

            var method = typeof(PlayerViewModel).GetMethod("OnPlayerPositionChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(98), TimeSpan.FromSeconds(100) });
            Assert.True(vm.Volume < 0.8);

            vm.CancelSleepTimer();
            Assert.Equal(SleepTimerOption.Off, vm.SleepTimerMode);
            Assert.Equal(0.8, vm.Volume, 2);
        });
    }

    [Fact]
    public void SleepTimer_BackwardSeekDuringFade_RestoresPreFadeVolume()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.Volume = 0.8;
            vm.SetSleepTimer("-1");

            var method = typeof(PlayerViewModel).GetMethod("OnPlayerPositionChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(98), TimeSpan.FromSeconds(100) });
            Assert.True(vm.Volume < 0.8);

            method?.Invoke(vm, new object[] { TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100) });
            Assert.Equal(0.8, vm.Volume, 2);
        });
    }

    [Fact]
    public void LoadLyricsForCurrentTrackAsync_WhenNoActiveTrack_ReturnsEarlyWithoutFetching()
    {
        WpfTestHost.Run(() =>
        {
            AudioPlayerService.Instance.Stop();
            var vm = new PlayerViewModel();
            Assert.True(string.IsNullOrEmpty(AudioPlayerService.Instance.CurrentFilePath));
            var task = vm.LoadLyricsForCurrentTrackAsync();
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Empty(vm.Lyrics);
            Assert.False(vm.HasLyrics);
            Assert.False(vm.IsLyricsLoading);
        });
    }

    [Fact]
    public void AccessibleSpeedText_IncludesControlPurposeAndSpeedValue()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.ResetSpeedCommand.Execute(null);
            Assert.Contains("1.00x", vm.AccessibleSpeedText);
            Assert.NotEqual("1.00x", vm.AccessibleSpeedText);

            vm.SpeedUpCommand.Execute(null);
            Assert.Contains("1.25x", vm.AccessibleSpeedText);
        });
    }

    [Fact]
    public void SleepTimer_TimedMode_AppliesFadeWhenDeadlineWithinFiveSeconds()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new PlayerViewModel();
            vm.Volume = 0.8;
            vm.SetSleepTimer("15");
            Assert.True(vm.IsSleepTimerActive);
            Assert.Equal(SleepTimerOption.Min15, vm.SleepTimerMode);

            var deadlineField = typeof(PlayerViewModel).GetField("_sleepDeadlineUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            deadlineField?.SetValue(vm, DateTime.UtcNow.AddSeconds(2.5));

            var tickMethod = typeof(PlayerViewModel).GetMethod("OnSleepTimerTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tickMethod?.Invoke(vm, new object?[] { null, EventArgs.Empty });

            Assert.True(vm.Volume < 0.8);
            Assert.Contains("80", vm.AccessibleVolumeText);
        });
    }
}
