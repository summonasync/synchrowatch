namespace Syncrowatch.Core.Engine;

public sealed record WatcherFailure(string Message, Exception? Exception);
