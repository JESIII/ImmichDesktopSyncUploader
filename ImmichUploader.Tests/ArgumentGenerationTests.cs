using ImmichUploader;
using Xunit;

namespace ImmichUploader.Tests;

public class ArgumentGenerationTests
{
    private static AppConfig NewConfig() => new()
    {
        Server = "http://immich.local:2283",
        ApiKey = "SECRET-KEY",
        AdminApiKey = "",
        Folders = new List<string> { @"C:\Pictures" },
        Concurrency = 4,
        ClientTimeoutMinutes = 60
    };

    [Fact]
    public void BuildInvocation_EmitsCuratedFlags()
    {
        var cfg = NewConfig();
        var invocation = ImmichGoRunner.BuildInvocation(cfg, @"C:\Pictures", DateTime.UtcNow.AddDays(-7));

        Assert.Equal("upload", invocation.Arguments[0]);
        Assert.Equal("from-folder", invocation.Arguments[1]);
        Assert.Contains("--no-ui", invocation.Arguments);
        Assert.Contains("--recursive", invocation.Arguments);
        Assert.Contains("--server=http://immich.local:2283", invocation.Arguments);
        Assert.Contains("--concurrent-tasks=4", invocation.Arguments);
        Assert.Contains("--on-errors=continue", invocation.Arguments);
        Assert.Contains("--manage-burst=Stack", invocation.Arguments);
        Assert.Contains("--manage-raw-jpeg=StackCoverRaw", invocation.Arguments);
        Assert.Contains("--client-timeout=60m", invocation.Arguments);
        Assert.Contains("--session-tag", invocation.Arguments);
        Assert.Contains("--pause-immich-jobs=false", invocation.Arguments);
        Assert.Equal(@"C:\Pictures", invocation.Arguments[^1]);
    }

    [Fact]
    public void BuildInvocation_KeepsSecretsOutOfArgumentsAndLogs()
    {
        var cfg = NewConfig();
        cfg.AdminApiKey = "ADMIN-SECRET";

        var invocation = ImmichGoRunner.BuildInvocation(cfg, @"C:\Pictures", DateTime.UtcNow);

        Assert.DoesNotContain(invocation.Arguments, a => a.Contains("SECRET-KEY"));
        Assert.DoesNotContain(invocation.Arguments, a => a.Contains("ADMIN-SECRET"));
        Assert.DoesNotContain("SECRET-KEY", invocation.DisplayCommand);
        Assert.DoesNotContain("ADMIN-SECRET", invocation.DisplayCommand);
        Assert.Equal("SECRET-KEY", invocation.Environment[ImmichGoRunner.ApiKeyEnvVar]);
        Assert.Equal("ADMIN-SECRET", invocation.Environment[ImmichGoRunner.AdminApiKeyEnvVar]);
    }

    [Fact]
    public void BuildInvocation_DisablesPauseWithoutAdminKey_AndWarns()
    {
        var cfg = NewConfig();
        cfg.PauseImmichJobs = true;

        var invocation = ImmichGoRunner.BuildInvocation(cfg, @"C:\Pictures", DateTime.UtcNow);

        Assert.Contains("--pause-immich-jobs=false", invocation.Arguments);
        Assert.Contains(invocation.Warnings, w => w.Contains("Admin API key"));
        Assert.False(invocation.Environment.ContainsKey(ImmichGoRunner.AdminApiKeyEnvVar));
    }

    [Fact]
    public void BuildInvocation_EnablesPauseWithAdminKeyAndOptIn()
    {
        var cfg = NewConfig();
        cfg.AdminApiKey = "ADMIN";
        cfg.PauseImmichJobs = true;

        var invocation = ImmichGoRunner.BuildInvocation(cfg, @"C:\Pictures", DateTime.UtcNow);

        Assert.Contains("--pause-immich-jobs=true", invocation.Arguments);
        Assert.Empty(invocation.Warnings);
    }

    [Fact]
    public void BuildInvocation_ClampsConcurrentTasks()
    {
        var cfg = NewConfig();

        cfg.Concurrency = 99;
        Assert.Contains("--concurrent-tasks=20", ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow).Arguments);

        cfg.Concurrency = 0;
        Assert.Contains("--concurrent-tasks=1", ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow).Arguments);
    }

    [Fact]
    public void BuildInvocation_FullScanUsesFarPastDate()
    {
        var cfg = NewConfig();
        var now = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.MinValue, now);

        var range = invocation.Arguments.First(a => a.StartsWith("--date-range="));
        Assert.Equal("--date-range=1976-01-15,2026-01-15", range);
    }

    [Fact]
    public void BuildInvocation_RespectsOptionalToggles()
    {
        var cfg = NewConfig();
        cfg.SkipSslVerify = true;
        cfg.OnErrors = "stop";
        cfg.SessionTag = false;

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow);

        Assert.Contains("--skip-verify-ssl", invocation.Arguments);
        Assert.Contains("--on-errors=stop", invocation.Arguments);
        Assert.DoesNotContain("--session-tag", invocation.Arguments);
    }

    [Fact]
    public void BuildInvocation_EmitsSimpleOrganizationFlags()
    {
        var cfg = NewConfig();
        cfg.FolderAsAlbum = "FOLDER";
        cfg.IntoAlbum = "Family Archive";
        cfg.ManageHeicJpeg = "StackCoverHeic";

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow);

        Assert.Contains("--folder-as-album=FOLDER", invocation.Arguments);
        Assert.Contains("--into-album=Family Archive", invocation.Arguments);
        Assert.Contains("--manage-heic-jpeg=StackCoverHeic", invocation.Arguments);
    }

    [Fact]
    public void BuildInvocation_EmitsAdvancedFlags()
    {
        var cfg = NewConfig();
        cfg.IgnoreSidecarFiles = true;
        cfg.ManageEpsonFastFoto = true;
        cfg.FolderAsTags = true;
        cfg.ApiTrace = true;
        cfg.DeviceUuid = "device-1";
        cfg.TimeZone = "UTC";
        cfg.AlbumPathJoiner = " / ";
        cfg.IncludeType = "VIDEO";
        cfg.IncludeExtensions = ".jpg,.heic";
        cfg.ExcludeExtensions = ".thm";
        cfg.BanFiles = "@eaDir/\n.DS_Store";
        cfg.Tags = "vacation, family";
        cfg.LogLevel = "DEBUG";

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow);

        Assert.Contains("--ignore-sidecar-files", invocation.Arguments);
        Assert.Contains("--manage-epson-fastfoto", invocation.Arguments);
        Assert.Contains("--folder-as-tags", invocation.Arguments);
        Assert.Contains("--api-trace", invocation.Arguments);
        Assert.Contains("--device-uuid=device-1", invocation.Arguments);
        Assert.Contains("--time-zone=UTC", invocation.Arguments);
        Assert.Contains("--album-path-joiner= / ", invocation.Arguments);
        Assert.Contains("--include-type=VIDEO", invocation.Arguments);
        Assert.Contains("--include-extensions=.jpg,.heic", invocation.Arguments);
        Assert.Contains("--exclude-extensions=.thm", invocation.Arguments);
        Assert.Contains("--ban-file=@eaDir/", invocation.Arguments);
        Assert.Contains("--ban-file=.DS_Store", invocation.Arguments);
        Assert.Contains("--tag=vacation", invocation.Arguments);
        Assert.Contains("--tag=family", invocation.Arguments);
        Assert.Contains("--log-level=DEBUG", invocation.Arguments);
    }

    [Fact]
    public void BuildInvocation_EmitsExplicitFalseForTrueDefaults()
    {
        var cfg = NewConfig();
        cfg.Recursive = false;
        cfg.DateFromName = false;

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow);

        Assert.Contains("--recursive=false", invocation.Arguments);
        Assert.Contains("--date-from-name=false", invocation.Arguments);
    }

    [Fact]
    public void BuildInvocation_OmitsOptionalFlagsAtDefaults()
    {
        var cfg = NewConfig();

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow);

        Assert.DoesNotContain(invocation.Arguments, a => a.StartsWith("--folder-as-album"));
        Assert.DoesNotContain(invocation.Arguments, a => a.StartsWith("--manage-heic-jpeg"));
        Assert.DoesNotContain(invocation.Arguments, a => a.StartsWith("--include-type"));
        Assert.DoesNotContain(invocation.Arguments, a => a.StartsWith("--log-level"));
        Assert.DoesNotContain(invocation.Arguments, a => a.StartsWith("--ban-file"));
        Assert.DoesNotContain(invocation.Arguments, a => a.StartsWith("--tag"));
        Assert.DoesNotContain("--overwrite", invocation.Arguments);
        Assert.DoesNotContain("--dry-run", invocation.Arguments);
        Assert.DoesNotContain("--date-from-name=false", invocation.Arguments);
    }

    [Fact]
    public void BuildInvocation_WarnsForOverwriteAndDryRun()
    {
        var cfg = NewConfig();
        cfg.Overwrite = true;
        cfg.DryRun = true;

        var invocation = ImmichGoRunner.BuildInvocation(cfg, "x", DateTime.UtcNow);

        Assert.Contains("--overwrite", invocation.Arguments);
        Assert.Contains("--dry-run", invocation.Arguments);
        Assert.Contains(invocation.Warnings, w => w.Contains("Overwrite"));
        Assert.Contains(invocation.Warnings, w => w.Contains("Dry-run"));
    }
}
