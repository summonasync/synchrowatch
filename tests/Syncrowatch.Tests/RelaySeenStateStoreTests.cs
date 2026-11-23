using Shouldly;
using Syncrowatch.Core;
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
        var snapshot = second.Load();

        snapshot.RecoveredFromCorruption.ShouldBeFalse();
        snapshot.Packages.Count.ShouldBe(2);
        snapshot.Packages[0].RelayPath.ShouldBe("alpha/fix-pack.synchr");
        snapshot.Packages[0].BlobSha.ShouldBe("a1b2c3d4");
        snapshot.Packages[1].RelayPath.ShouldBe("beta/pack.synchr");
    }

    [Fact]
    public void Load_WithoutSavedState_ReturnsEmpty()
    {
        new RelaySeenStateStore(_directory).Load().Packages.ShouldBeEmpty();
    }

    [Fact]
    public void Load_CorruptStateFile_QuarantinesAsBakAndReportsRecovery()
    {
        Directory.CreateDirectory(_directory);
        var stateFile = Path.Combine(_directory, RelaySeenStateStore.FileName);
        File.WriteAllText(stateFile, "{ not json");

        var snapshot = new RelaySeenStateStore(_directory).Load();

        snapshot.RecoveredFromCorruption.ShouldBeTrue();
        snapshot.Packages.ShouldBeEmpty();
        File.Exists(stateFile).ShouldBeFalse();
        File.Exists(stateFile + ".bak").ShouldBeTrue();
        File.ReadAllText(stateFile + ".bak").ShouldBe("{ not json");
    }

    [Fact]
    public async Task Load_AfterQuarantine_CyclesCleanlyOnEmptyState()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, RelaySeenStateStore.FileName), "{ not json");
        var store = new RelaySeenStateStore(_directory);
        _ = store.Load();

        await store.Save([new RelaySeenPackage("alpha/pack.synchr", "1111")], CancellationToken.None);
        var recovered = store.Load();

        recovered.RecoveredFromCorruption.ShouldBeFalse();
        recovered.Packages.ShouldHaveSingleItem().BlobSha.ShouldBe("1111");
    }

    [Fact]
    public async Task Save_OverwritesPreviousState()
    {
        var store = new RelaySeenStateStore(_directory);
        await store.Save([new RelaySeenPackage("alpha/pack.synchr", "1111")], CancellationToken.None);
        await store.Save([new RelaySeenPackage("alpha/pack.synchr", "2222")], CancellationToken.None);

        new RelaySeenStateStore(_directory).Load().Packages.Single().BlobSha.ShouldBe("2222");
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
