using System.Text.RegularExpressions;

namespace Syncrowatch.Core.Transport;

public static class RemoteUrlRedactor
{
    private static readonly Regex RemoteUrlPattern = new(@"(?:https?|ssh)://[^\s'""']+", RegexOptions.Compiled);

    public static string Scrub(string message) => RemoteUrlPattern.Replace(message, "[redacted-remote]");
}
