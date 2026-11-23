using Syncrowatch.Core;

namespace Syncrowatch.Core.Autostart;

public sealed class XdgDesktopAutostartManager : IAutostartManager
{
    public const string FileName = "syncrowatch.desktop";

    private readonly string _directory;

    public XdgDesktopAutostartManager(string? directory = null) =>
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");

    public string FilePath => Path.Combine(_directory, FileName);

    public Result<Unit> Enable(string exePath)
    {
        var tempPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(tempPath, BuildEntryContent(exePath));
            File.Move(tempPath, FilePath, true);
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "autostart entry could not be written"));
        }
    }

    public Result<Unit> Disable()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "autostart entry could not be deleted"));
        }
    }

    public Result<bool> IsApplied() => new Result<bool>.Ok(File.Exists(FilePath));

    public static string BuildExecValue(string exePath)
    {
        if (exePath.AsSpan().IndexOfAny([' ', '\t', '"', '\\']) < 0)
        {
            return exePath;
        }

        return $"\"{exePath.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string BuildEntryContent(string exePath) =>
        string.Join('\n',
            "[Desktop Entry]",
            "Type=Application",
            "Exec=" + BuildExecValue(exePath),
            "Name=Syncrowatch",
            "X-GNOME-Autostart-enabled=true");
}
