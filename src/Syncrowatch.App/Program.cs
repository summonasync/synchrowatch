using Avalonia;

namespace Syncrowatch.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (TryHandleVersion(args, Console.Out))
        {
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    internal static bool TryHandleVersion(string[] args, TextWriter writer)
    {
        if (args.Length > 0 && args[0] is "--version" or "-v")
        {
            writer.WriteLine($"syncrowatch {AppVersion}");
            return true;
        }

        return false;
    }

    private static string AppVersion =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
