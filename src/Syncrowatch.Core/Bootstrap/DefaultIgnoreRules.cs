namespace Syncrowatch.Core.Bootstrap;

public static class DefaultIgnoreRules
{
    public static IReadOnlyList<string> Patterns { get; } =
    [
        ".git/",
        ".hg/",
        ".svn/",
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
}
