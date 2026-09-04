namespace SnapSort.App.Models;

public sealed record AnalysisResult(
    string Path,
    string Sha256,
    string PerceptualHash,
    double Sharpness,
    int QualityScore,
    int Width,
    int Height,
    DateTime? DateTaken,
    double[]? Embedding,
    int Orientation,
    double OrientationConfidence,
    double SecondBestOrientationConfidence,
    int SuggestedRotation);

public sealed record SimilarityGroup(int Id, string Type, IReadOnlyList<PhotoItem> Photos);
