namespace CazadorYTM.Tests;

using System;
using System.IO;
using Xunit;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;

public class AppConfigTests : IDisposable
{
    private readonly string _tempConfigPath;

    public AppConfigTests()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempConfigPath))
        {
            try { File.Delete(_tempConfigPath); } catch { }
        }
    }

    [Fact]
    public void AppConfig_GetSet_PersistsValues()
    {
        var config = new AppConfig(_tempConfigPath);
        config.Set("format", "flac");
        config.Set("max_concurrent", 5);
        config.Save();

        var loaded = new AppConfig(_tempConfigPath);
        Assert.Equal("flac", loaded.Get("format", "mp3"));
        Assert.Equal(5, loaded.Get("max_concurrent", 3));
    }

    [Fact]
    public void AppConfig_GetDefault_WhenKeyMissing()
    {
        var config = new AppConfig(_tempConfigPath);
        Assert.Equal("default_val", config.Get("non_existent", "default_val"));
        Assert.Equal(42, config.Get("missing_int", 42));
    }
}

public class HistoryManagerTests : IDisposable
{
    private readonly string _tempHistoryPath;

    public HistoryManagerTests()
    {
        _tempHistoryPath = Path.Combine(Path.GetTempPath(), $"test_history_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempHistoryPath))
        {
            try { File.Delete(_tempHistoryPath); } catch { }
        }
    }

    [Fact]
    public void HistoryManager_AddAndGetEntries_WorksCorrectly()
    {
        var history = new HistoryManager(_tempHistoryPath);
        history.AddEntry(new DownloadEntry
        {
            Title = "Test Song 1",
            Url = "https://youtube.com/watch?v=1",
            FilePath = "C:/Music/1.mp3"
        });
        history.AddEntry(new DownloadEntry
        {
            Title = "Test Song 2",
            Url = "https://youtube.com/watch?v=2",
            FilePath = "C:/Music/2.mp3"
        });

        var entries = history.GetEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Test Song 2", entries[0].Title); // Most recent first
    }

    [Fact]
    public void HistoryManager_RemoveEntry_RemovesTarget()
    {
        var history = new HistoryManager(_tempHistoryPath);
        var entry = new DownloadEntry
        {
            Title = "To Delete",
            Url = "https://youtube.com/watch?v=del",
            FilePath = "C:/Music/del.mp3"
        };
        history.AddEntry(entry);
        Assert.Single(history.GetEntries());

        history.RemoveEntry(entry);
        Assert.Empty(history.GetEntries());
    }

    [Fact]
    public void HistoryManager_Clear_EmptiesAll()
    {
        var history = new HistoryManager(_tempHistoryPath);
        history.Add("Song A", "urlA", "mp3", "OK");
        history.Add("Song B", "urlB", "flac", "OK");
        Assert.Equal(2, history.GetEntries().Count);

        history.Clear();
        Assert.Empty(history.GetEntries());
    }

    [Fact]
    public void HistoryManager_RemoveEntries_RemovesMultipleEntries()
    {
        var history = new HistoryManager(_tempHistoryPath);
        var entry1 = new DownloadEntry { Title = "Song 1", Url = "url1", FilePath = "C:/Music/1.mp3" };
        var entry2 = new DownloadEntry { Title = "Song 2", Url = "url2", FilePath = "C:/Music/2.mp3" };
        var entry3 = new DownloadEntry { Title = "Song 3", Url = "url3", FilePath = "C:/Music/3.mp3" };

        history.AddEntry(entry1);
        history.AddEntry(entry2);
        history.AddEntry(entry3);
        Assert.Equal(3, history.GetEntries().Count);

        var removed = history.RemoveEntries(new[] { entry1, entry3 });
        Assert.Equal(2, removed);

        var remaining = history.GetEntries();
        Assert.Single(remaining);
        Assert.Equal("Song 2", remaining[0].Title);
    }
}
