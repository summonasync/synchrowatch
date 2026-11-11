using Shouldly;
using Syncrowatch.Core.Engine.Conflicts;
using Xunit;

namespace Syncrowatch.Tests;

public class BinaryContentSnifferTests
{
    [Fact]
    public void NullByte_InFirstBytes_IsBinary() =>
        BinaryContentSniffer.IsBinary([0x50, 0x4B, 0x00, 0x01]).ShouldBeTrue();

    [Fact]
    public void NullByte_AtProbeBoundary_IsBinary()
    {
        var content = new byte[BinaryContentSniffer.ProbeLength + 10];
        content[^1] = 0x2F;
        BinaryContentSniffer.IsBinary(content).ShouldBeTrue();
    }

    [Fact]
    public void PlainText_IsNotBinary() =>
        BinaryContentSniffer.IsBinary("hello world\nsecond line\n"u8.ToArray()).ShouldBeFalse();

    [Fact]
    public void Utf8MultibyteText_IsNotBinary() =>
        BinaryContentSniffer.IsBinary("naïve résumé — данные ✓"u8.ToArray()).ShouldBeFalse();

    [Fact]
    public void NullByte_BeyondProbeWindow_IsText()
    {
        var content = new byte[BinaryContentSniffer.ProbeLength + 4];
        Array.Fill(content, (byte)0x41);
        "text only"u8.ToArray().CopyTo(content, 0);
        content[^1] = 0x00;
        BinaryContentSniffer.IsBinary(content).ShouldBeFalse();
    }

    [Fact]
    public void EmptyContent_IsNotBinary() => BinaryContentSniffer.IsBinary([]).ShouldBeFalse();
}
