using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Synchro.Core;
using Syncrowatch.Core;

namespace Syncrowatch.Core.Config;

public sealed class CredentialStore : ICredentialStore
{
    private static readonly UnixFileMode OwnerOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _filePath;

    public CredentialStore(string? directory = null) => _filePath = SyncrowatchPaths.CredentialFile(directory);

    public string FilePath => _filePath;

    public async Task<Result<PersonalAccessToken>> Read(CancellationToken cancellationToken)
    {
        try
        {
            var content = await ReadTokenAsync(cancellationToken).ConfigureAwait(false);
            return new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(content));
        }
        catch (CredentialStoreException ex)
        {
            return new Result<PersonalAccessToken>.Fail(ex.Error);
        }
    }

    public async Task<Result<PersonalAccessToken>> Write(PersonalAccessToken token, CancellationToken cancellationToken)
    {
        try
        {
            await WriteTokenAsync(token, cancellationToken).ConfigureAwait(false);
            return new Result<PersonalAccessToken>.Ok(token);
        }
        catch (CredentialStoreException ex)
        {
            return new Result<PersonalAccessToken>.Fail(ex.Error);
        }
    }

    public Task<Result<Unit>> Delete(CancellationToken cancellationToken)
    {
        try
        {
            DeleteTokenFile();
            return Task.FromResult<Result<Unit>>(new Result<Unit>.Ok(Unit.Value));
        }
        catch (CredentialStoreException ex)
        {
            return Task.FromResult<Result<Unit>>(new Result<Unit>.Fail(ex.Error));
        }
    }

    private async Task<string> ReadTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new CredentialNotFoundException(_filePath);
            }

            return content.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CredentialNotFoundException(_filePath, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CredentialStoreUnavailableException("credential file could not be read", ex);
        }
    }

    private async Task WriteTokenAsync(PersonalAccessToken token, CancellationToken cancellationToken)
    {
        var tempPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = new FileStream(tempPath, CreateOptions()))
            {
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(token.Value.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            RestrictAccess(tempPath);
            File.Move(tempPath, _filePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CredentialStoreUnavailableException("credential file could not be written", ex);
        }
    }

    private void DeleteTokenFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                throw new CredentialNotFoundException(_filePath);
            }

            File.Delete(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CredentialStoreUnavailableException("credential file could not be deleted", ex);
        }
    }

    private FileStreamOptions CreateOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyMode;
        }

        return options;
    }

    private void RestrictAccess(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User;
                if (user is null)
                {
                    throw new SecurityException("current windows identity exposes no user sid");
                }

                var file = new FileInfo(path);
                var security = file.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                security.SetAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                file.SetAccessControl(security);
                return;
            }

            File.SetUnixFileMode(path, OwnerOnlyMode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or IdentityNotMappedException)
        {
            throw new CredentialStoreUnavailableException("credential file permissions could not be restricted", ex);
        }
    }
}
