namespace Syncrowatch.App.History;

public delegate Task ConflictAction(string relativePath, Syncrowatch.Core.Engine.Conflicts.ConflictOption option);
