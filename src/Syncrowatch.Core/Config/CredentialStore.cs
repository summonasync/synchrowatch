using System.Security.AccessControl;
using System.Security.Principal;
using Synchro.Core;

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
            var content = await ReadTokenAsync(cancellationToken);
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
            await WriteTokenAsync(token, cancellationToken);
            return new Result<PersonalAccessToken>.Ok(token);
        }
        catch (CredentialStoreException ex)
        {
            return new Result<PersonalAccessToken>.Fail(ex.Error);
        }
    }

    public async Task<Result<PersonalAccessToken>> Delete(CancellationToken cancellationToken)
    {
        try
        {
            var content = await DeleteTokenAsync(cancellationToken);
            return new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(content));
        }
        catch (CredentialStoreException ex)
        {
            return new Result<PersonalAccessToken>.Fail(ex.Error);
        }
    }

    private async Task<string> ReadTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(_filePath, cancellationToken);
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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = new FileStream(_filePath, CreateOptions()))
            {
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(token.Value.AsMemory(), cancellationToken);
            }

            RestrictAccess();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CredentialStoreUnavailableException("credential file could not be written", ex);
        }
    }

    private async Task<string> DeleteTokenAsync(CancellationToken cancellationToken)
    {
        var content = await ReadTokenAsync(cancellationToken);
        try
        {
            File.Delete(_filePath);
            return content;
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

    private void RestrictAccess()
    {
        if (OperatingSystem.IsWindows())
        {
            var file = new FileInfo(_filePath);
            var security = file.GetAccessControl();
            security.SetAccessRuleProtection(true, false);
            security.SetAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            file.SetAccessControl(security);
            return;
        }

        File.SetUnixFileMode(_filePath, OwnerOnlyMode);
    }
}
