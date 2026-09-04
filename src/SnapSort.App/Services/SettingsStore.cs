using System.IO;
using System.Text.Json;
using SnapSort.App.Models;

namespace SnapSort.App.Services;

public sealed class SettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(AppPaths.DataDir, "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
