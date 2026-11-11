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

    public IReadOnlyList<RelaySeenPackage> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            try
            {
                using var stream = File.OpenRead(_filePath);
                return JsonSerializer.Deserialize<List<RelaySeenPackage>>(stream, SerializerOptions) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return [];
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
}
