using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Xunit;

namespace Syncrowatch.Tests;

public class SyncEngineSeamTests
{
    [Fact]
    public async Task SyncOnce_SubstitutedEngine_ReturnsConfiguredCycleReport()
    {
        var engine = Substitute.For<ISyncEngine>();
        var report = new SyncCycleReport(
            DateTimeOffset.Parse("2026-11-10T12:00:00Z"),
            Pulled: new SyncOutcome("main", "a1b2c3", "d4e5f6"),
            Pushed: null);
        engine.SyncOnce(Arg.Any<CancellationToken>()).Returns(new Result<SyncCycleReport>.Ok(report));

        var outcome = await engine.SyncOnce(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        ok.Value.Pulled.ShouldNotBeNull().ToSha.ShouldBe("d4e5f6");
        ok.Value.Pushed.ShouldBeNull();
    }

    [Fact]
    public async Task SyncOnce_FailingEngine_CarriesEngineError()
    {
        var engine = Substitute.For<ISyncEngine>();
        engine.SyncOnce(Arg.Any<CancellationToken>()).Returns(new Result<SyncCycleReport>.Fail(SyncError.Conflict("diverged from remote")));

        var outcome = await engine.SyncOnce(CancellationToken.None);

        var fail = outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>();
        fail.Error.Code.ShouldBe("conflict");
        fail.Error.Message.ShouldContain("diverged");
    }

    [Fact]
    public async Task GetStatus_SubstitutedEngine_ReportsIdleState()
    {
        var engine = Substitute.For<ISyncEngine>();
        engine.GetStatus(Arg.Any<CancellationToken>()).Returns(new Result<EngineStatus>.Ok(new EngineStatus(false, null, null)));

        var outcome = await engine.GetStatus(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<EngineStatus>.Ok>();
        ok.Value.Running.ShouldBeFalse();
        ok.Value.LastCycleUtc.ShouldBeNull();
    }
}
