using System.Security;
using Microsoft.Win32;
using Synchro.Core;

namespace Syncrowatch.Core.Autostart;

public sealed class WindowsRunKeyAutostartManager : IAutostartManager
{
    public const string ValueName = "Syncrowatch";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string BuildRunKeyData(string exePath) => $"\"{exePath}\"";

    public Result<Unit> Enable(string exePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Unsupported();
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return new Result<Unit>.Fail(new SyncError("unavailable", "registry run key could not be opened or created"));
            }

            runKey.SetValue(ValueName, BuildRunKeyData(exePath));
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "run key could not be written"));
        }
    }

    public Result<Unit> Disable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Unsupported();
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return new Result<Unit>.Fail(new SyncError("unavailable", "registry run key could not be opened or created"));
            }

            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", "run key could not be deleted"));
        }
    }

    public Result<bool> IsApplied()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Result<bool>.Fail(new SyncError("unsupported", "windows run key autostart requires windows"));
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (runKey is null)
            {
                return new Result<bool>.Fail(new SyncError("unavailable", "registry run key could not be opened or created"));
            }

            return new Result<bool>.Ok(runKey.GetValue(ValueName) is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new Result<bool>.Fail(new SyncError("unavailable", "run key could not be read"));
        }
    }

    private static Result<Unit> Unsupported() =>
        new Result<Unit>.Fail(new SyncError("unsupported", "windows run key autostart requires windows"));
}
