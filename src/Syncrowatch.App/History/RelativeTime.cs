namespace Syncrowatch.App.History;

public static class RelativeTime
{
    public static string Format(DateTimeOffset occurredUtc, DateTimeOffset nowUtc)
    {
        var elapsed = nowUtc - occurredUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays}d ago";
        }

        return occurredUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
