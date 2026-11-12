using Shouldly;
using Syncrowatch.App.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(90, "1m ago")]
    [InlineData(3500, "58m ago")]
    [InlineData(7200, "2h ago")]
    [InlineData(172800, "2d ago")]
    [InlineData(518400, "6d ago")]
    public void Format_MapElapsedSecondsToRelativeLabels(int elapsedSeconds, string expected)
    {
        RelativeTime.Format(Now.AddSeconds(-elapsedSeconds), Now).ShouldBe(expected);
    }

    [Fact]
    public void Format_WeekOldEvent_FallsBackToAbsoluteDate()
    {
        var occurred = new DateTimeOffset(2026, 10, 3, 9, 30, 0, TimeSpan.Zero);

        RelativeTime.Format(occurred, Now).ShouldNotBe("just now");
        RelativeTime.Format(occurred, Now).ShouldContain("2026-10-03");
    }

    [Fact]
    public void Format_ClockSkewInTheFuture_ClampsToJustNow()
    {
        RelativeTime.Format(Now.AddSeconds(120), Now).ShouldBe("just now");
    }
}
