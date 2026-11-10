namespace Syncrowatch.Core.Engine;

public sealed class PathspecFile : IDisposable
{
    private PathspecFile(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public static PathspecFile Create(IReadOnlyList<string> relativePaths)
    {
        var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "syncrowatch-pathspec-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(filePath, string.Join('\0', relativePaths));
        return new PathspecFile(filePath);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
