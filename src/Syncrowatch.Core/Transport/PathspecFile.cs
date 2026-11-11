namespace Syncrowatch.Core.Transport;

public sealed class PathspecFile : IDisposable
{
    private PathspecFile(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public static PathspecFile Create(IReadOnlyList<string> relativePaths)
    {
        var filePath = Path.Combine(Path.GetTempPath(), "syncrowatch-pathspec-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(filePath, string.Join('\0', relativePaths.Select(WithLiteralMagic)));
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

    private static string WithLiteralMagic(string relativePath) => ":(literal)" + relativePath;
}
