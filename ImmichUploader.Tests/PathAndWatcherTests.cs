using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

public class PathAndWatcherTests
{
    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("clip.MP4", true)]
    [InlineData("raw.CR3", true)]
    [InlineData("pack.heic", true)]
    [InlineData("notes.txt", false)]
    [InlineData("archive.zip", false)]
    public void MediaFileClassifier_RecognizesMedia(string name, bool expected)
    {
        Assert.Equal(expected, MediaFileClassifier.IsMediaFile(name));
    }

    [Fact]
    public void ShouldWatchPath_RejectsSiblingFolder()
    {
        var folders = new[] { @"C:\photos" };

        Assert.True(PathFilters.ShouldWatchPath(folders, @"C:\photos\2026\a.jpg"));
        Assert.False(PathFilters.ShouldWatchPath(folders, @"C:\photos_backup\a.jpg"));
    }

    [Fact]
    public void ShouldWatchPath_RejectsExcludedDirectoriesAndNonMedia()
    {
        var folders = new[] { @"C:\photos" };

        Assert.False(PathFilters.ShouldWatchPath(folders, @"C:\photos\thumbnails\a.jpg"));
        Assert.False(PathFilters.ShouldWatchPath(folders, @"C:\photos\@eaDir\a.jpg"));
        Assert.False(PathFilters.ShouldWatchPath(folders, @"C:\photos\a.txt"));
    }

    [Fact]
    public void FindOwningFolder_ReturnsContainingFolder()
    {
        var folders = new[] { @"C:\photos", @"C:\videos" };

        Assert.Equal(@"C:\videos", PathFilters.FindOwningFolder(folders, @"C:\videos\sub\clip.mp4"));
        Assert.Null(PathFilters.FindOwningFolder(folders, @"C:\other\clip.mp4"));
    }

    [Fact]
    public void FolderWatcher_ShouldAccept_UsesConfigFilters()
    {
        using var temp = new TempFolder();
        var cfg = new AppConfig { Folders = new List<string> { temp.Path } };
        using var watcher = new FolderWatcher(cfg);

        var media = temp.CreateFile("a.jpg");
        var text = temp.CreateFile("a.txt");

        Assert.True(watcher.ShouldAccept(media));
        Assert.False(watcher.ShouldAccept(text));
    }

    [Fact]
    public void Watcher_Handle_QueuesOnlyWhenRunning()
    {
        using var temp = new TempFolder();
        var cfg = new AppConfig
        {
            Folders = new List<string> { temp.Path },
            WatcherEnabled = true,
            WatcherDebounceSeconds = 60
        };
        using var watcher = new FolderWatcher(cfg);
        var media = temp.CreateFile("a.jpg");

        // Not started yet: must not queue, but config acceptance still applies.
        Assert.False(watcher.Handle(media));
        Assert.True(watcher.ShouldAccept(media));
        Assert.Equal(0, watcher.PendingCount);
    }

    [Fact]
    public void FileReadiness_TrueForSettledFile_FalseForMissing()
    {
        using var temp = new TempFolder();
        var file = temp.CreateFile("a.jpg", "data");

        Assert.True(FileReadiness.IsReady(file));
        Assert.False(FileReadiness.IsReady(System.IO.Path.Combine(temp.Path, "missing.jpg")));
    }
}
