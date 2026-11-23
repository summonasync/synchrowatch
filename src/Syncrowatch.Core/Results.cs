namespace Syncrowatch.Core;

public sealed record SyncError(string Code, string Message)
{
    public static SyncError NotFound(string message) => new("not_found", message);

    public static SyncError Invalid(string message) => new("invalid", message);

    public static SyncError Conflict(string message) => new("conflict", message);
}

public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;

    public sealed record Fail(SyncError Error) : Result<T>;
}
