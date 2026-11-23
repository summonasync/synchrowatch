using NSubstitute;
using Shouldly;
using Syncrowatch.Core;
using Xunit;

namespace Syncrowatch.Tests;

public class RemoteRepositorySeamTests
{
    private static readonly RemoteValidation Validation = new(true, "main");

    [Fact]
    public async Task ValidateRepository_SubstitutedRemote_PassesThroughConfiguredResult()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.ValidateRepository(Arg.Any<CancellationToken>()).Returns(new Result<RemoteValidation>.Ok(Validation));

        var outcome = await remote.ValidateRepository(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<RemoteValidation>.Ok>();
        ok.Value.Reachable.ShouldBeTrue();
        ok.Value.DefaultBranch.ShouldBe("main");
    }

    [Fact]
    public async Task Push_SubstitutedRemote_ReportsBranchMove()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Push("main", Arg.Any<CancellationToken>()).Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "a1b2c3", "d4e5f6")));

        var outcome = await remote.Push("main", CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncOutcome>.Ok>();
        ok.Value.Branch.ShouldBe("main");
        ok.Value.FromSha.ShouldBe("a1b2c3");
        ok.Value.ToSha.ShouldBe("d4e5f6");
    }

    [Fact]
    public async Task PushForceWithLease_SubstitutedRemote_ReceivesExpectedSha()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var expected = new SyncOutcome("main", "a1b2c3", "d4e5f6");
        remote.PushForceWithLease("main", "a1b2c3", Arg.Any<CancellationToken>()).Returns(new Result<SyncOutcome>.Ok(expected));

        var outcome = await remote.PushForceWithLease("main", "a1b2c3", CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncOutcome>.Ok>();
        ok.Value.ShouldBe(expected);
        await remote.Received(1).PushForceWithLease("main", "a1b2c3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListBranch_SubstitutedRemote_ReturnsRefsCollection()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var refs = new List<RemoteRef> { new("main", "a1b2c3"), new("backup", "d4e5f6") };
        remote.ListBranch(Arg.Any<CancellationToken>()).Returns(new Result<IReadOnlyList<RemoteRef>>.Ok(refs));

        var outcome = await remote.ListBranch(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<IReadOnlyList<RemoteRef>>.Ok>();
        ok.Value.Count.ShouldBe(2);
        ok.Value.ShouldContain(r => r.Name == "backup");
    }
}
