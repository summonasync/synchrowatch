namespace Syncrowatch.Core.Transport;

public static class GitHubRemoteLink
{
    private const string GithubHost = "github.com";

    public static string? TryParseBrowserBase(string? remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (!string.Equals(uri.Host, GithubHost, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort)
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return null;
        }

        var (owner, repo) = (segments[0], segments[1]);
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        return repo.Length == 0 ? null : $"https://{GithubHost}/{owner}/{repo}";
    }

    public static string BuildLink(string browserBase, string sha, string relativePath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        return $"{browserBase}/blob/{sha}/{path}";
    }
}
