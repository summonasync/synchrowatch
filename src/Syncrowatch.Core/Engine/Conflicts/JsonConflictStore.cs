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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<ConflictRecord> _records;

    public JsonConflictStore(string? directory = null)
    {
        _filePath = SyncrowatchPaths.ConflictStateFile(directory);
        _records = LoadExisting();
    }

    public async Task<Result<IReadOnlyList<ConflictRecord>>> All()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return new Result<IReadOnlyList<ConflictRecord>>.Ok([.. _records]);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<ConflictRecord>>> Pending()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = _records
                .Where(record => record.Status is ConflictRecordStatus.Pending or ConflictRecordStatus.Staged)
                .OrderBy(record => record.DetectedUtc)
                .ThenBy(record => record.Path, StringComparer.Ordinal)
                .ToList();
            return new Result<IReadOnlyList<ConflictRecord>>.Ok(pending);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Unit>> Save(ConflictRecord record)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
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

            return await PersistAsync([.. _records]).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<Unit>> PersistAsync(List<ConflictRecord> snapshot)
    {
        var tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
            TryCleanup(tempPath);
            return new Result<Unit>.Fail(new SyncError("unavailable", "conflict state file could not be written"));
        }
    }

    private static void TryCleanup(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
