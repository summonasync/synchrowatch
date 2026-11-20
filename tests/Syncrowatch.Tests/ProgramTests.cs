using System.IO;
using Shouldly;
using Syncrowatch.App;
using Xunit;

namespace Syncrowatch.Tests;

public class ProgramTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void TryHandleVersion_VersionFlagAsFirstArg_WritesVersionAndReturnsTrue(string flag)
    {
        using var writer = new StringWriter();
        var expected = $"syncrowatch {typeof(Program).Assembly.GetName().Version?.ToString(3)}{Environment.NewLine}";

        var handled = Program.TryHandleVersion([flag], writer);

        handled.ShouldBeTrue();
        writer.ToString().ShouldBe(expected);
    }

    [Fact]
    public void TryHandleVersion_EmptyArgs_ReturnsFalseWithoutOutput()
    {
        using var writer = new StringWriter();

        var handled = Program.TryHandleVersion([], writer);

        handled.ShouldBeFalse();
        writer.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void TryHandleVersion_NonVersionFirstArg_ReturnsFalseWithoutOutput()
    {
        using var writer = new StringWriter();

        var handled = Program.TryHandleVersion(["sync", "--version"], writer);

        handled.ShouldBeFalse();
        writer.ToString().ShouldBeEmpty();
    }
}
