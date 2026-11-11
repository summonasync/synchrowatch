using System.Text.Json;
using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Relay;

public sealed class RelaySeenStateStore(string? directory = null) : IRelaySeenState
{
    public const string FileName = "relay-seen.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath = Path.Combine(directory ?? SyncrowatchPaths.UserDataDirectory(), FileName);
    private readonly object _gate = new();

    public RelaySeenSnapshot Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                return new RelaySeenSnapshot([], false);
            }

            try
            {
                using var stream = File.OpenRead(_filePath);
                var packages = JsonSerializer.Deserialize<List<RelaySeenPackage>>(stream, SerializerOptions) ?? [];
                return new RelaySeenSnapshot(packages, false);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Quarantine();
                return new RelaySeenSnapshot([], true);
            }
        }
    }

    public async Task<Result<Unit>> Save(IReadOnlyList<RelaySeenPackage> packages, CancellationToken cancellationToken)
    {
        List<RelaySeenPackage> snapshot;
        lock (_gate)
        {
            snapshot = [.. packages];
        }

        var tempPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, true);
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "relay seen state could not be written"));
        }
    }

    private void Quarantine()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Move(_filePath, _filePath + ".bak", true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
