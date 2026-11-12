using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class SyncConfigValidationTests
{
    public static TheoryData<string> AcceptableUrls => new()
    {
        "https://github.com/peer/notes.git",
        "https://GitHub.com/peer/notes.git",
        "ssh://git@github.com/peer/notes.git",
        "git@github.com:peer/notes.git",
        "git@gitlab.example.org:22/peer/notes.git"
    };

    public static TheoryData<string> RejectedUrls => new()
    {
        "",
        "   ",
        "/tmp/local-bare.git",
        "../relative/bare.git",
        "file:///tmp/local-bare.git",
        "http://github.com/peer/notes.git",
        "ext::sh -c git-upload-pack",
        "-smuggled/path",
        "--upload-pack=touch /tmp/x",
        "https://",
        "ssh://no-dot-host/repo.git",
        "github.com/peer/notes.git",
        "main:backup",
        "git@github.com:",
        "host@:path"
    };

    [Theory]
    [MemberData(nameof(AcceptableUrls))]
    public void Validate_AcceptableUrl_Passes(string url)
    {
        SyncConfigValidation.Validate(SampleConfig(url)).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(RejectedUrls))]
    public void Validate_RejectedUrl_ReportsTypedInvalidError(string url)
    {
        var problem = SyncConfigValidation.Validate(SampleConfig(url));

        problem.ShouldNotBeNull();
        problem.Code.ShouldBe("invalid");
        problem.Message.ShouldContain("remote url");
    }

    [Fact]
    public void Validate_BlankRemoteUrl_ReportsTypedInvalidError()
    {
        var problem = SyncConfigValidation.Validate(SampleConfig(null!));

        problem.ShouldNotBeNull();
        problem.Code.ShouldBe("invalid");
    }

    [Fact]
    public void Validate_BlankWatchedFolder_ReportsFolderProblem()
    {
        var config = SampleConfig("https://github.com/peer/notes.git") with { WatchedFolder = "" };

        SyncConfigValidation.Validate(config)!.Message.ShouldContain("watched folder");
    }

    [Fact]
    public void Validate_DebounceOutOfRange_ReportsDebounceProblem()
    {
        var config = SampleConfig("https://github.com/peer/notes.git") with { Debounce = TimeSpan.FromSeconds(5) };

        SyncConfigValidation.Validate(config)!.Message.ShouldContain("debounce");
    }

    [Fact]
    public void Validate_UndefinedRunMode_ReportsModeProblem()
    {
        var config = SampleConfig("https://github.com/peer/notes.git") with { Mode = (SyncRunMode)42 };

        SyncConfigValidation.Validate(config)!.Message.ShouldContain("sync mode");
    }

    private static SyncConfig SampleConfig(string url) => new()
    {
        WatchedFolder = "/home/peer/notes",
        RemoteUrl = url,
        Branch = "main",
        Mode = SyncRunMode.Auto
    };
}
