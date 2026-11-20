using System.Text.Json;
using System.Text.Json.Serialization;
using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.History;

public sealed class JsonSyncEventStore : ISyncEventStore
{
    public const int MaxRetained = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly List<SyncEvent> _events;
    private long _sequence;

    public JsonSyncEventStore(string? directory = null, TimeProvider? timeProvider = null)
    {
        _filePath = SyncrowatchPaths.HistoryFile(directory);
        _time = timeProvider ?? TimeProvider.System;
        _events = LoadExisting();
        _sequence = _events.Count > 0 ? _events[^1].Sequence : 0;
    }

    public async Task<Result<SyncEvent>> Append(SyncEventDraft draft, CancellationToken cancellationToken)
    {
        SyncEvent appended;
        lock (_gate)
        {
            appended = new SyncEvent(++_sequence, _time.GetUtcNow(), draft.Kind, draft.Branch, draft.Detail, draft.Paths);
            _events.Add(appended);
            if (_events.Count > MaxRetained)
            {
                _events.RemoveRange(0, _events.Count - MaxRetained);
            }
        }

        var persisted = await PersistAsync(cancellationToken).ConfigureAwait(false);
        return persisted switch
        {
            Result<Unit>.Ok => new Result<SyncEvent>.Ok(appended),
            Result<Unit>.Fail failure => new Result<SyncEvent>.Fail(failure.Error),
            _ => throw new InvalidOperationException("history persistence is neither ok nor fail")
        };
    }

    public Task<Result<IReadOnlyList<SyncEvent>>> Query(HistoryQuery query, CancellationToken cancellationToken)
    {
        List<SyncEvent> snapshot;
        lock (_gate)
        {
            snapshot = [.. _events];
        }

        IEnumerable<SyncEvent> selected = snapshot;
        if (query.Kinds is { } kinds)
        {
            selected = selected.Where(candidate => kinds.Contains(candidate.Kind));
        }

        if (query.Since is { } since)
        {
            selected = selected.Where(candidate => candidate.OccurredUtc >= since);
        }

        var newest = selected.Reverse().Take(Math.Max(query.Limit, 0)).ToList();
        return Task.FromResult<Result<IReadOnlyList<SyncEvent>>>(new Result<IReadOnlyList<SyncEvent>>.Ok(newest));
    }

    private async Task<Result<Unit>> PersistAsync(CancellationToken cancellationToken)
    {
        List<SyncEvent> snapshot;
        lock (_gate)
        {
            snapshot = [.. _events];
        }

        var tempPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, true);
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "history file could not be written"));
        }
    }

    private List<SyncEvent> LoadExisting()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<List<SyncEvent>>(stream, SerializerOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
