using System.Text.Json;
using Synchro.Core;

namespace Syncrowatch.Core.Config;

public sealed class JsonAppPreferencesStore : IAppPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonAppPreferencesStore(string? directory = null) => _filePath = SyncrowatchPaths.PreferencesFile(directory);

    public string FilePath => _filePath;

    public async Task<Result<AppPreferences>> Load(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Result<AppPreferences>.Ok(new AppPreferences());
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var preferences = await JsonSerializer.DeserializeAsync<AppPreferences>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return new Result<AppPreferences>.Ok(preferences ?? new AppPreferences());
        }
        catch (JsonException)
        {
            return new Result<AppPreferences>.Ok(new AppPreferences());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<AppPreferences>.Fail(new SyncError("unavailable", "preferences file could not be read"));
        }
    }

    public async Task<Result<AppPreferences>> Save(AppPreferences preferences, CancellationToken cancellationToken)
    {
        var tempPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, true);
            return new Result<AppPreferences>.Ok(preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<AppPreferences>.Fail(new SyncError("unavailable", "preferences file could not be written"));
        }
    }
}
