using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Relay;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class RelaySeenStateStoreTests : IDisposable
{
    private readonly string _directory = TempDir.Create();

    [Fact]
    public async Task Save_ThenLoadFromFreshStore_RoundTripsPackages()
    {
        var first = new RelaySeenStateStore(_directory);
        await first.Save(
            [new RelaySeenPackage("alpha/fix-pack.synchr", "a1b2c3d4"), new RelaySeenPackage("beta/pack.synchr", "e5f6")],
            CancellationToken.None);

        var second = new RelaySeenStateStore(_directory);
        var loaded = second.Load();

        loaded.Count.ShouldBe(2);
        loaded[0].RelayPath.ShouldBe("alpha/fix-pack.synchr");
        loaded[0].BlobSha.ShouldBe("a1b2c3d4");
        loaded[1].RelayPath.ShouldBe("beta/pack.synchr");
    }

    [Fact]
    public void Load_WithoutSavedState_ReturnsEmpty()
    {
        new RelaySeenStateStore(_directory).Load().ShouldBeEmpty();
    }

    [Fact]
    public void Load_CorruptStateFile_ReturnsEmptyInsteadOfThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, RelaySeenStateStore.FileName), "{ not json");

        new RelaySeenStateStore(_directory).Load().ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_OverwritesPreviousState()
    {
        var store = new RelaySeenStateStore(_directory);
        await store.Save([new RelaySeenPackage("alpha/pack.synchr", "1111")], CancellationToken.None);
        await store.Save([new RelaySeenPackage("alpha/pack.synchr", "2222")], CancellationToken.None);

        new RelaySeenStateStore(_directory).Load().Single().BlobSha.ShouldBe("2222");
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
