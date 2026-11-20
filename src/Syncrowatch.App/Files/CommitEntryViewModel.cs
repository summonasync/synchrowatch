using Syncrowatch.App.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.App.Files;

public sealed class CommitEntryViewModel(GitLogEntry entry, DateTimeOffset nowUtc, bool isLatest = false)
{
    private const int ShortShaLength = 7;

    public string Author { get; } = entry.AuthorName;

    public string TimeText { get; } = RelativeTime.Format(entry.WhenUtc, nowUtc);

    public string Subject { get; } = entry.Subject;

    public string ShortSha { get; } = entry.Sha.Length <= ShortShaLength ? entry.Sha : entry.Sha[..ShortShaLength];

    public bool IsLatest { get; } = isLatest;
}
