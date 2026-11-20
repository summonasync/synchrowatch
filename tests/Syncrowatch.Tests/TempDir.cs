namespace Syncrowatch.Tests;

public static class TempDir
{
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "syncrowatch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
