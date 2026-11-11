namespace Syncrowatch.Core.Engine.Conflicts;

public static class BinaryContentSniffer
{
    public const int ProbeLength = 8000;

    public static bool IsBinary(byte[] content)
    {
        var probe = Math.Min(content.Length, ProbeLength);
        for (var index = 0; index < probe; index++)
        {
            if (content[index] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
