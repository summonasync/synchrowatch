using System.Text.Json;
using System.Text.Json.Serialization;
using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Engine.Conflicts;

public sealed class JsonConflictStore : IConflictStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private List<ConflictRecord> _records;

    public JsonConflictStore(string? directory = null)
    {
        _filePath = SyncrowatchPaths.ConflictStateFile(directory);
        _records = LoadExisting();
    }

    public Task<Result<IReadOnlyList<ConflictRecord>>> All()
    {
        lock (_gate)
        {
            return Task.FromResult<Result<IReadOnlyList<ConflictRecord>>>(
                new Result<IReadOnlyList<ConflictRecord>>.Ok([.. _records]));
        }
    }

    public Task<Result<IReadOnlyList<ConflictRecord>>> Pending()
    {
        lock (_gate)
        {
            var pending = _records
                .Where(record => record.Status is ConflictRecordStatus.Pending or ConflictRecordStatus.Staged)
                .OrderBy(record => record.DetectedUtc)
                .ThenBy(record => record.Path, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<Result<IReadOnlyList<ConflictRecord>>>(
                new Result<IReadOnlyList<ConflictRecord>>.Ok(pending));
        }
    }

    public async Task<Result<Unit>> Save(ConflictRecord record)
    {
        List<ConflictRecord> snapshot;
        lock (_gate)
        {
            var existing = _records.FindIndex(stored => stored.Path == record.Path);
            if (existing >= 0)
            {
                _records[existing] = record;
            }
            else
            {
                _records.Add(record);
            }

            snapshot = [.. _records];
        }

        return await PersistAsync(snapshot).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> PersistAsync(List<ConflictRecord> snapshot)
    {
        var tempPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, true);
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "conflict state file could not be written"));
        }
    }

    private List<ConflictRecord> LoadExisting()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<List<ConflictRecord>>(stream, SerializerOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
