using System.Text.Json;
using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

public class ConfigMigrationTests
{
    [Fact]
    public void Normalize_ClampsAndDefaults()
    {
        var cfg = new AppConfig
        {
            Concurrency = 500,
            MaxParallelProcesses = 0,
            DaysBack = -5,
            ClientTimeoutMinutes = 0,
            WatcherDebounceSeconds = 0,
            MonthlyRescanDay = 40,
            WeeklyHour = 99,
            OnErrors = "bogus",
            ManageBurst = "nope",
            ManageRawJpeg = "nope",
            Folders = new List<string> { " a ", "a", "" }
        };

        cfg.Normalize();

        Assert.Equal(20, cfg.Concurrency);
        Assert.Equal(1, cfg.MaxParallelProcesses);
        Assert.Equal(1, cfg.DaysBack);
        Assert.Equal(1, cfg.ClientTimeoutMinutes);
        Assert.Equal(1, cfg.WatcherDebounceSeconds);
        Assert.Equal(28, cfg.MonthlyRescanDay);
        Assert.Equal(23, cfg.WeeklyHour);
        Assert.Equal("continue", cfg.OnErrors);
        Assert.Equal("Stack", cfg.ManageBurst);
        Assert.Equal("StackCoverRaw", cfg.ManageRawJpeg);
        Assert.Single(cfg.Folders);
        Assert.Equal("a", cfg.Folders[0]);
    }

    [Theory]
    [InlineData("STOP", "stop")]
    [InlineData("continue", "continue")]
    [InlineData("", "continue")]
    [InlineData("5", "5")]
    [InlineData("-1", "continue")]
    [InlineData("banana", "continue")]
    public void NormalizeOnErrors_ValidatesValues(string input, string expected)
    {
        Assert.Equal(expected, AppConfig.NormalizeOnErrors(input));
    }

    [Fact]
    public void OldConfigJson_UpgradesToCurrentSchema()
    {
        // Simulates a v1 config.json written before the new settings existed.
        const string json =
            "{ \"Server\": \"http://h\", \"ApiKey\": \"k\", \"Folders\": [\"C:\\\\Pictures\"], \"Concurrency\": 4, \"DaysBack\": 7 }";

        var cfg = JsonSerializer.Deserialize<AppConfig>(json);
        Assert.NotNull(cfg);

        cfg!.Normalize();

        Assert.Equal(AppConfig.CurrentConfigVersion, cfg.ConfigVersion);
        Assert.Equal(3, cfg.MaxParallelProcesses);   // new default
        Assert.False(cfg.PauseImmichJobs);           // opt-in by design
        Assert.False(cfg.WatcherEnabled);            // opt-in by design
        Assert.True(cfg.WeeklyEnabled);
        Assert.True(cfg.MonthlyEnabled);
        Assert.Equal("continue", cfg.OnErrors);
        Assert.Equal(30, cfg.WatcherDebounceSeconds);
        Assert.Equal("NONE", cfg.FolderAsAlbum);
        Assert.Equal("NoStack", cfg.ManageHeicJpeg);
        Assert.Equal("all", cfg.IncludeType);
        Assert.Equal("", cfg.LogLevel);
        Assert.True(cfg.Recursive);
        Assert.True(cfg.DateFromName);
    }

    [Fact]
    public void Normalize_NewFlagChoicesAndLists()
    {
        var cfg = new AppConfig
        {
            FolderAsAlbum = "bogus",
            ManageHeicJpeg = "bogus",
            IncludeType = "bogus",
            LogLevel = "bogus",
            IncludeExtensions = " .jpg , .heic ,, ",
            BanFiles = " @eaDir/ \n\n .DS_Store ",
            Tags = "a, b ,, c"
        };

        cfg.Normalize();

        Assert.Equal("NONE", cfg.FolderAsAlbum);
        Assert.Equal("NoStack", cfg.ManageHeicJpeg);
        Assert.Equal("all", cfg.IncludeType);
        Assert.Equal("", cfg.LogLevel);
        Assert.Equal(".jpg,.heic", cfg.IncludeExtensions);
        Assert.Equal("@eaDir/\n.DS_Store", cfg.BanFiles);
        Assert.Equal("a,b,c", cfg.Tags);
    }

    [Fact]
    public void Normalize_LogLevelAcceptsKnownValuesCaseInsensitively()
    {
        var cfg = new AppConfig { LogLevel = "debug" };

        cfg.Normalize();

        Assert.Equal("DEBUG", cfg.LogLevel);
    }
}
