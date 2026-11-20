namespace Syncrowatch.Core.Engine.Conflicts;

public static class DeletionGuard
{
    public static bool Survives(ConflictItem item, string rootFolder)
    {
        return item.Kind switch
        {
            ConflictKind.DeletedLocallyModifiedRemotely => File.Exists(Locate(rootFolder, item.CopyPath)),
            ConflictKind.ModifiedLocallyDeletedRemotely => File.Exists(Locate(rootFolder, item.Path)),
            _ => true
        };
    }

    private static string Locate(string rootFolder, string? relativePath) =>
        Path.Combine(rootFolder, (relativePath ?? string.Empty).Replace('\\', '/'));
}
