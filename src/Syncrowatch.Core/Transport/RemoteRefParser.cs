using Syncrowatch.Core;

namespace Syncrowatch.Core.Transport;

public static class RemoteRefParser
{
    private const string HeadsPrefix = "refs/heads/";
    private const string TrackingPrefix = "refs/remotes/origin/";

    public static IReadOnlyList<RemoteRef> ParseHeads(string output)
    {
        var heads = new List<RemoteRef>();
        foreach (var (sha, refName) in TabbedLines(output))
        {
            if (!refName.StartsWith(HeadsPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            heads.Add(new RemoteRef(refName[HeadsPrefix.Length..], sha));
        }

        return heads;
    }

    public static IReadOnlyList<RemoteRef> ParseTracking(string output)
    {
        var tracked = new List<RemoteRef>();
        foreach (var (sha, refName) in TabbedLines(output))
        {
            if (!refName.StartsWith(TrackingPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = refName[TrackingPrefix.Length..];
            if (name.Length > 0 && name != "HEAD")
            {
                tracked.Add(new RemoteRef(name, sha));
            }
        }

        return tracked;
    }

    public static string? ParseDefaultBranch(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith("ref: ", StringComparison.Ordinal))
            {
                continue;
            }

            var refName = line["ref: ".Length..].Split('\t')[0].Trim();
            if (refName.StartsWith(HeadsPrefix, StringComparison.Ordinal))
            {
                return refName[HeadsPrefix.Length..];
            }
        }

        return null;
    }

    private static IEnumerable<(string Sha, string RefName)> TabbedLines(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var tab = trimmed.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            yield return (trimmed[..tab].Trim(), trimmed[(tab + 1)..].Trim());
        }
    }
}
