using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Xunit;

namespace Syncrowatch.Tests;

public class SyncConfigStoreSeamTests
{
    private static readonly SyncConfig Config = new()
    {
        WatchedFolder = "/home/peer/notes",
        RemoteUrl = "git@github.com:peer/notes.git",
        Branch = "main",
        Mode = Synchro.Core.SyncMode.Git,
        PollInterval = TimeSpan.FromSeconds(30)
    };

    [Fact]
    public async Task Load_SubstitutedStore_ReturnsSavedConfig()
    {
        var store = Substitute.For<ISyncConfigStore>();
        store.Load(Arg.Any<CancellationToken>()).Returns(new Result<SyncConfig>.Ok(Config));

        var outcome = await store.Load(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncConfig>.Ok>();
        ok.Value.WatchedFolder.ShouldBe("/home/peer/notes");
        ok.Value.RemoteUrl.ShouldBe("git@github.com:peer/notes.git");
        ok.Value.Mode.ShouldBe(Synchro.Core.SyncMode.Git);
        ok.Value.PollInterval.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Save_SubstitutedStore_EchoesPersistedConfig()
    {
        var store = Substitute.For<ISyncConfigStore>();
        store.Save(Config, Arg.Any<CancellationToken>()).Returns(new Result<SyncConfig>.Ok(Config));

        var outcome = await store.Save(Config, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncConfig>.Ok>();
        ok.Value.ShouldBe(Config);
        await store.Received(1).Save(Config, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Load_MissingStore_ReturnsInvalidFailure()
    {
        var store = Substitute.For<ISyncConfigStore>();
        store.Load(Arg.Any<CancellationToken>()).Returns(new Result<SyncConfig>.Fail(SyncError.Invalid("no syncrowatch config in watched folder")));

        var outcome = await store.Load(CancellationToken.None);

        var fail = outcome.ShouldBeOfType<Result<SyncConfig>.Fail>();
        fail.Error.Code.ShouldBe("invalid");
    }
}
