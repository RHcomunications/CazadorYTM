namespace CazadorYTM.Gui.Tests;

using CazadorYTM.Core.Services;
using CazadorYTM.Gui.ViewModels;

public class RadarViewModelTests
{
    private static RadarItem Item(string id, string title, string? artist) => new()
    {
        Id = id,
        Title = title,
        Artist = artist,
        Section = RadarSection.History
    };

    [Fact]
    public void Filter_ByTitle_KeepsOnlyMatches()
    {
        var vm = new RadarViewModel();
        vm.AllItems.Add(Item("1", "Bohemian Rhapsody", "Queen"));
        vm.AllItems.Add(Item("2", "Imagine", "John Lennon"));

        vm.FilterQuery = "bohemian";

        Assert.Single(vm.FilteredItems);
        Assert.Equal("1", vm.FilteredItems[0].Id);
    }

    [Fact]
    public void Filter_ByArtist_KeepsMatches()
    {
        var vm = new RadarViewModel();
        vm.AllItems.Add(Item("1", "Bohemian Rhapsody", "Queen"));
        vm.AllItems.Add(Item("2", "Imagine", "John Lennon"));

        vm.FilterQuery = "lennon";

        Assert.Single(vm.FilteredItems);
        Assert.Equal("2", vm.FilteredItems[0].Id);
    }

    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var vm = new RadarViewModel();
        vm.AllItems.Add(Item("1", "Bohemian Rhapsody", "Queen"));

        vm.FilterQuery = "QUEEN";

        Assert.Single(vm.FilteredItems);
    }

    [Fact]
    public void Filter_Cleared_RestoresAllItems()
    {
        var vm = new RadarViewModel();
        vm.AllItems.Add(Item("1", "Bohemian Rhapsody", "Queen"));
        vm.AllItems.Add(Item("2", "Imagine", "John Lennon"));

        vm.FilterQuery = "zzz-no-match";
        Assert.Empty(vm.FilteredItems);

        vm.FilterQuery = string.Empty;
        Assert.Equal(2, vm.FilteredItems.Count);
    }

    [Fact]
    public void Filter_WithNoSelection_SelectsFirstMatch()
    {
        var vm = new RadarViewModel();
        vm.AllItems.Add(Item("1", "Bohemian Rhapsody", "Queen"));
        vm.AllItems.Add(Item("2", "Imagine", "John Lennon"));

        vm.FilterQuery = "imagine";

        Assert.NotNull(vm.SelectedItem);
        Assert.Equal("2", vm.SelectedItem!.Id);
    }

    [Fact]
    public void Filter_WhenSelectionFilteredOut_ReselectsFirstRemaining()
    {
        var vm = new RadarViewModel();
        var first = Item("1", "Bohemian Rhapsody", "Queen");
        var second = Item("2", "Imagine", "John Lennon");
        vm.AllItems.Add(first);
        vm.AllItems.Add(second);
        vm.SelectedItem = second;

        vm.FilterQuery = "bohemian";

        Assert.Same(first, vm.SelectedItem);
    }

    [Fact]
    public void BackToPlaylists_WhenNotViewingDetails_DoesNothing()
    {
        var vm = new RadarViewModel();
        var statusBefore = vm.StatusText;

        vm.BackToPlaylists();

        Assert.False(vm.IsViewingPlaylistDetails);
        Assert.Equal(statusBefore, vm.StatusText);
    }

    [Fact]
    public void BackToPlaylists_WhenViewingDetails_ExitsDetailsMode()    {
        var vm = new RadarViewModel();
        vm.IsViewingPlaylistDetails = true;
        vm.CurrentPlaylistTitle = "Mi lista favorita";

        vm.BackToPlaylists();

        Assert.False(vm.IsViewingPlaylistDetails);
        Assert.Null(vm.CurrentPlaylist);
        Assert.Equal(string.Empty, vm.CurrentPlaylistTitle);
        Assert.Contains("Mis Listas", vm.StatusText);
    }

    [Fact]
    public void HasSelectedTrack_IsFalseForPlaylist()
    {
        var vm = new RadarViewModel();
        var playlist = Item("VLPL1", "Mi lista", null);
        playlist.IsPlaylist = true;

        vm.SelectedItem = playlist;

        Assert.False(vm.HasSelectedTrack);
        Assert.False(vm.LikeSelectedCommand.CanExecute(null));
        Assert.False(vm.DislikeSelectedCommand.CanExecute(null));
        Assert.False(vm.ClearRatingCommand.CanExecute(null));
        Assert.False(vm.AddToPlaylistCommand.CanExecute(null));
    }

    [Fact]
    public void HasSelectedTrack_IsTrueForTrackAndEnablesCommands()
    {
        var vm = new RadarViewModel();

        vm.SelectedItem = Item("1", "Bohemian Rhapsody", "Queen");

        Assert.True(vm.HasSelectedTrack);
        Assert.True(vm.LikeSelectedCommand.CanExecute(null));
        Assert.True(vm.DislikeSelectedCommand.CanExecute(null));
        Assert.True(vm.ClearRatingCommand.CanExecute(null));
        Assert.True(vm.AddToPlaylistCommand.CanExecute(null));
    }
}
