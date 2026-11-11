using System.Text;
using System.Text.RegularExpressions;

namespace Syncrowatch.Core.Engine;

public sealed class SyncIgnoreRules
{
    private const string IgnoreFileName = ".syncignore";

    private static readonly string[] BuiltIn =
    [
        ".git",
        ".git/",
        ".svn/",
        ".hg/",
        "bin/",
        "obj/",
        ".idea/",
        ".vs/",
        ".synchro/",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
        "*.tmp",
        "~$*",
        "*.part",
        "*.crdownload"
    ];

    private readonly List<IgnorePattern> _patterns;

    private SyncIgnoreRules(IEnumerable<string> lines) =>
        _patterns = [.. lines.Select(IgnorePattern.Parse).Where(pattern => pattern is not null).Select(pattern => pattern!)];

    public static SyncIgnoreRules ForFolder(string rootFolder)
    {
        var lines = new List<string>(BuiltIn);
        var ignoreFile = Path.Combine(rootFolder, IgnoreFileName);
        if (File.Exists(ignoreFile))
        {
            lines.AddRange(File.ReadAllLines(ignoreFile));
        }

        return new SyncIgnoreRules(lines);
    }

    public static SyncIgnoreRules FromLines(IEnumerable<string> lines) => new(lines);

    public bool IsIgnored(string relativePath)
    {
        var segments = relativePath.Split('/');
        for (var depth = 1; depth < segments.Length; depth++)
        {
            if (Decide(string.Join('/', segments, 0, depth), isDirectory: true) == IgnoreDecision.Ignored)
            {
                return true;
            }
        }

        return Decide(relativePath, isDirectory: false) == IgnoreDecision.Ignored;
    }

    private IgnoreDecision Decide(string path, bool isDirectory)
    {
        var decision = IgnoreDecision.None;
        foreach (var pattern in _patterns)
        {
            if (pattern.Matches(path, isDirectory))
            {
                decision = pattern.Negated ? IgnoreDecision.Included : IgnoreDecision.Ignored;
            }
        }

        return decision;
    }

    private enum IgnoreDecision
    {
        None,
        Ignored,
        Included
    }

    private sealed record IgnorePattern(bool Negated, bool DirectoryOnly, Regex Matcher)
    {
        public static IgnorePattern? Parse(string line)
        {
            var pattern = line.Trim();
            if (pattern.Length == 0 || pattern.StartsWith('#'))
            {
                return null;
            }

            var negated = pattern.StartsWith('!');
            if (negated)
            {
                pattern = pattern[1..];
            }

            var directoryOnly = pattern.EndsWith('/');
            if (directoryOnly)
            {
                pattern = pattern[..^1];
            }

            var anchored = pattern.Contains('/');
            if (pattern.StartsWith('/'))
            {
                pattern = pattern[1..];
            }

            if (pattern.Length == 0)
            {
                return null;
            }

            return new IgnorePattern(negated, directoryOnly, Compile(pattern, anchored));
        }

        public bool Matches(string path, bool isDirectory)
        {
            if (DirectoryOnly && !isDirectory)
            {
                return false;
            }

            return Matcher.IsMatch(path);
        }

        private static Regex Compile(string pattern, bool anchored)
        {
            var source = new StringBuilder();
            if (!anchored)
            {
                source.Append("(?:.*/)?");
            }

            var index = 0;
            while (index < pattern.Length)
            {
                var character = pattern[index];
                switch (character)
                {
                    case '*' when pattern[index..].StartsWith("**/"):
                        source.Append("(?:.*/)?");
                        index += 3;
                        continue;
                    case '*' when pattern[index..].StartsWith("**"):
                        source.Append(".*");
                        index += 2;
                        continue;
                    case '*':
                        source.Append("[^/]*");
                        break;
                    case '?':
                        source.Append("[^/]");
                        break;
                    case '[' when pattern.IndexOf(']', index + 1) > index:
                        var set = pattern[index..(pattern.IndexOf(']', index + 1) + 1)];
                        source.Append(set.StartsWith("[!") ? "[^" + set[2..] : set);
                        index = pattern.IndexOf(']', index + 1);
                        break;
                    default:
                        source.Append(Regex.Escape(character.ToString()));
                        break;
                }

                index++;
            }

            var options = OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None;
            return new Regex($"^{source}$", options | RegexOptions.CultureInvariant);
        }
    }
}
