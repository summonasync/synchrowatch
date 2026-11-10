using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Transport;

public static class GitArgumentGuards
{
    public static string RefName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Contains(':'))
        {
            throw new GitOperationException(SyncError.Invalid($"ref '{name}' is not a usable ref name"));
        }

        return name;
    }

    public static string Sha(string sha)
    {
        if (string.IsNullOrEmpty(sha) || sha.Contains(' ') || !sha.All(char.IsAsciiHexDigit))
        {
            throw new GitOperationException(SyncError.Invalid("expected remote sha is not a hexadecimal object id"));
        }

        return sha;
    }

    public static void UsableRemoteUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('-'))
        {
            throw new GitOperationException(SyncError.Invalid("remote url is not a usable git target"));
        }
    }

    public static void AcceptableRemoteUrl(string url)
    {
        if (!SyncConfigValidation.IsAcceptableRemoteUrl(url))
        {
            throw new GitOperationException(SyncError.Invalid("remote url must be an https or ssh git address"));
        }
    }

    public static void ConfigValue(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.Any(char.IsControl))
        {
            throw new GitOperationException(SyncError.Invalid($"{fieldName} is not a usable git config value"));
        }
    }

    public static string RelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var escapesRoot = normalized.StartsWith('/') || normalized.StartsWith("../", StringComparison.Ordinal)
            || relativePath.Contains(':')
            || normalized.Split('/').Contains("..");

        if (relativePath.Length == 0 || normalized.StartsWith('-') || escapesRoot)
        {
            throw new GitOperationException(SyncError.Invalid($"path '{relativePath}' is not a usable repository path"));
        }

        return normalized;
    }
}
