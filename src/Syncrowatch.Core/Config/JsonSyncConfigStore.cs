using System.Text.Json;
using System.Text.Json.Serialization;
using Syncrowatch.Core;

namespace Syncrowatch.Core.Config;

public sealed class JsonSyncConfigStore : ISyncConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public JsonSyncConfigStore(string? directory = null) => _filePath = SyncrowatchPaths.SettingsFile(directory);

    public string FilePath => _filePath;

    public async Task<Result<SyncConfig>> Load(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Result<SyncConfig>.Fail(SyncError.NotFound("syncrowatch settings file does not exist yet"));
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var config = await JsonSerializer.DeserializeAsync<SyncConfig>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (config is null)
            {
                return new Result<SyncConfig>.Fail(SyncError.Invalid("settings file contains no configuration"));
            }

            return SyncConfigValidation.Validate(config) is { } problem
                ? new Result<SyncConfig>.Fail(problem)
                : new Result<SyncConfig>.Ok(config);
        }
        catch (JsonException ex)
        {
            return new Result<SyncConfig>.Fail(SyncError.Invalid($"settings file is not valid configuration ({ex.Message})"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<SyncConfig>.Fail(new SyncError("unavailable", "settings file could not be read"));
        }
    }

    public async Task<Result<SyncConfig>> Save(SyncConfig config, CancellationToken cancellationToken)
    {
        if (SyncConfigValidation.Validate(config) is { } problem)
        {
            return new Result<SyncConfig>.Fail(problem);
        }

        var tempPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, true);
            return new Result<SyncConfig>.Ok(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<SyncConfig>.Fail(new SyncError("unavailable", "settings file could not be written"));
        }
    }
}
