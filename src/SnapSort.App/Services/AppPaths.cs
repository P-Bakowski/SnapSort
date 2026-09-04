using System.IO;

namespace SnapSort.App.Services;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SnapSort");

    public static string DbPath { get; } = Path.Combine(DataDir, "index.db");
    public static string ThumbnailDir { get; } = Path.Combine(DataDir, "Thumbnails");
}
