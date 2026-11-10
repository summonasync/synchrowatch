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
        if (sha.Length == 0 || sha.Contains(' ') || !sha.All(char.IsAsciiHexDigit))
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
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new GitOperationException(SyncError.Invalid($"{fieldName} is not a usable git config value"));
        }
    }
}
