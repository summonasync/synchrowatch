namespace Syncrowatch.Core.Bootstrap;

public sealed record IgnoreRules(IReadOnlyList<string> Patterns)
{
    private const string FileName = ".syncignore";

    public static IgnoreRules Default { get; } = new(DefaultIgnoreRules.Patterns);

    public static IgnoreRules Load(string folder)
    {
        var patterns = new List<string>(DefaultIgnoreRules.Patterns);
        var path = Path.Combine(folder, FileName);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var pattern = line.Trim();
                if (pattern.Length == 0 || pattern.StartsWith('#'))
                {
                    continue;
                }

                patterns.Add(pattern);
            }
        }

        return new IgnoreRules(patterns);
    }
}
