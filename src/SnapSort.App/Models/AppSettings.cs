namespace SnapSort.App.Models;

public sealed class AppSettings
{
    public string SimilaritySensitivity { get; set; } = "Normalna";
    public bool DetectBlur { get; set; } = true;
    public bool DetectDuplicates { get; set; } = true;
    public bool AutoAnalyze { get; set; } = true;
    public double ThumbnailSize { get; set; } = 188;
    public string Theme { get; set; } = "Systemowy";
    public string TrashFolderName { get; set; } = "_Kosz";
}
