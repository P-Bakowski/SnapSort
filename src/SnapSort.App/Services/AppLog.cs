using System.IO;

namespace SnapSort.App.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(AppPaths.DataDir, "Logs", "app.log");

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void Error(Exception ex, string context)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:O} {context}: {ex.Message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
