using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class CredentialStoreTests
{
    private const string Token = "ghp_t1credprobe_9f8e7d6c5b4a";

    [Fact]
    public async Task Write_Read_Delete_RoundTripsToken()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);

            var written = await store.Write(new PersonalAccessToken(Token), CancellationToken.None);
            written.ShouldBeOfType<Result<PersonalAccessToken>.Ok>();

            var read = await store.Read(CancellationToken.None);
            read.ShouldBeOfType<Result<PersonalAccessToken>.Ok>().Value.Value.ShouldBe(Token);

            var deleted = await store.Delete(CancellationToken.None);
            deleted.ShouldBeOfType<Result<Unit>.Ok>().Value.ShouldBe(Unit.Value);

            var gone = await store.Read(CancellationToken.None);
            gone.ShouldBeOfType<Result<PersonalAccessToken>.Fail>().Error.Code.ShouldBe("not_found");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Delete_RemovesCredentialFileFromDisk()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);
            await store.Write(new PersonalAccessToken(Token), CancellationToken.None);

            File.Exists(store.FilePath).ShouldBeTrue();

            await store.Delete(CancellationToken.None);

            File.Exists(store.FilePath).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Read_WithoutStoredToken_ReturnsNotFound()
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = await new CredentialStore(dir).Read(CancellationToken.None);

            var fail = outcome.ShouldBeOfType<Result<PersonalAccessToken>.Fail>();
            fail.Error.Code.ShouldBe("not_found");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Delete_WithoutStoredToken_ReturnsNotFound()
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = await new CredentialStore(dir).Delete(CancellationToken.None);

            var fail = outcome.ShouldBeOfType<Result<Unit>.Fail>();
            fail.Error.Code.ShouldBe("not_found");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Read_EmptyCredentialFile_ReturnsNotFound()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);
            await File.WriteAllTextAsync(store.FilePath, "   \n");

            var outcome = await store.Read(CancellationToken.None);

            var fail = outcome.ShouldBeOfType<Result<PersonalAccessToken>.Fail>();
            fail.Error.Code.ShouldBe("not_found");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Write_OverwritesPreviouslyStoredToken_WithoutTempResidue()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);
            await store.Write(new PersonalAccessToken(Token), CancellationToken.None);

            await store.Write(new PersonalAccessToken("ghp_rotated_1111"), CancellationToken.None);

            File.Exists(store.FilePath + ".tmp").ShouldBeFalse();
            var read = await store.Read(CancellationToken.None);
            read.ShouldBeOfType<Result<PersonalAccessToken>.Ok>().Value.Value.ShouldBe("ghp_rotated_1111");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Delete_UnreadableFile_OnUnix_RemovesItAnyway()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);
            await store.Write(new PersonalAccessToken(Token), CancellationToken.None);
            File.SetUnixFileMode(store.FilePath, UnixFileMode.None);

            var outcome = await store.Delete(CancellationToken.None);

            outcome.ShouldBeOfType<Result<Unit>.Ok>();
            File.Exists(store.FilePath).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Delete_StoredToken_ResultCarriesNoSecret()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);
            await store.Write(new PersonalAccessToken(Token), CancellationToken.None);

            var outcome = await store.Delete(CancellationToken.None);

            var ok = outcome.ShouldBeOfType<Result<Unit>.Ok>();
            ok.Value.ShouldBe(Unit.Value);
            ok.ToString().ShouldNotContain(Token);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Write_WhenDirectoryIsBlocked_ReturnsUnavailable()
    {
        var dir = TempDir.Create();
        try
        {
            var blocker = Path.Combine(dir, "blocker");
            await File.WriteAllTextAsync(blocker, "occupied by a file");
            var store = new CredentialStore(Path.Combine(blocker, "sub"));

            var outcome = await store.Write(new PersonalAccessToken(Token), CancellationToken.None);

            var fail = outcome.ShouldBeOfType<Result<PersonalAccessToken>.Fail>();
            fail.Error.Code.ShouldBe("unavailable");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task StoredFile_OnLinux_HasOwnerOnlyPermissions()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var dir = TempDir.Create();
        try
        {
            var store = new CredentialStore(dir);
            await store.Write(new PersonalAccessToken(Token), CancellationToken.None);

            File.GetUnixFileMode(store.FilePath)
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Token_DisplayString_MasksStoredValue()
    {
        new PersonalAccessToken(Token).ToString().ShouldNotContain(Token);
    }
}
