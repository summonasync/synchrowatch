namespace Syncrowatch.Core.Relay;

public sealed record RelayPackage(
    string Project,
    string PackageFile,
    string BlobSha,
    string RelayPath,
    string CliCommand);
