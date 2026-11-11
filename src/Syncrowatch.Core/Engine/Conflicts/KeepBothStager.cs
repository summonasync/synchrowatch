using System.Globalization;

namespace Syncrowatch.Core.Engine.Conflicts;

public sealed class KeepBothStager(string rootFolder, TimeProvider? timeProvider = null)
{
    private const string TimestampFormat = "yyyy-MM-dd HHmmss";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string MaterializeTheirsCopy(string relativePath, byte[] content) =>
        Materialize(relativePath, content, $"theirs {_time.GetUtcNow().ToString(TimestampFormat, CultureInfo.InvariantCulture)}");

    public string MaterializeMineCopy(string relativePath, byte[] content) =>
        Materialize(relativePath, content, "mine");

    public void DiscardCopy(string relativePath)
    {
        var fullPath = Path.Combine(rootFolder, relativePath.Replace('\\', '/'));
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(rootFolder), StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Materialize(string relativePath, byte[] content, string label)
    {
        var source = relativePath.Replace('\\', '/');
        var directory = Path.GetDirectoryName(source) ?? string.Empty;
        var (stem, extension) = SplitExtension(Path.GetFileName(source));
        var targetDirectory = directory.Length == 0 ? rootFolder : Path.Combine(rootFolder, directory);
        Directory.CreateDirectory(targetDirectory);
        var name = ResolveAvailableName(targetDirectory, stem, label, extension);
        var targetPath = directory.Length == 0 ? name : $"{directory}/{name}";
        File.WriteAllBytes(Path.Combine(rootFolder, targetPath.Replace('/', Path.DirectorySeparatorChar)), content);
        return targetPath;
    }

    private static string ResolveAvailableName(string targetDirectory, string stem, string label, string extension)
    {
        var candidate = $"{stem} ({label}){extension}";
        for (var attempt = 2; File.Exists(Path.Combine(targetDirectory, candidate)); attempt++)
        {
            candidate = $"{stem} ({label}) {attempt}{extension}";
        }

        return candidate;
    }

    private static (string Stem, string Extension) SplitExtension(string fileName)
    {
        var separator = fileName.LastIndexOf('.');
        return separator > 0
            ? (fileName[..separator], fileName[separator..])
            : (fileName, string.Empty);
    }
}
