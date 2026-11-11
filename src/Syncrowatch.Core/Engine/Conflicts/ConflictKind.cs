namespace Syncrowatch.Core.Engine.Conflicts;

public enum ConflictKind
{
    TextConflict,
    BinaryConflict,
    ModifiedLocallyDeletedRemotely,
    DeletedLocallyModifiedRemotely
}
