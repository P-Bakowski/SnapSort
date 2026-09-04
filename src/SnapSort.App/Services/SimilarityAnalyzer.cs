using SnapSort.App.Models;

namespace SnapSort.App.Services;

public static class SimilarityAnalyzer
{
    public static IReadOnlyList<SimilarityGroup> BuildSimilarShots(IReadOnlyList<PhotoItem> photos, string sensitivity = "Normalna")
    {
        var threshold = sensitivity switch
        {
            "Ścisła" => 0.94,
            "Luźna" => 0.82,
            _ => 0.88
        };

        var embedded = BuildEmbeddingGroups(photos, threshold, "Podobne ujęcia");
        if (embedded.Count > 0)
            return embedded;

        var prefix = sensitivity switch
        {
            "Ścisła" => 10,
            "Luźna" => 6,
            _ => 8
        };

        var groups = photos
            .Where(p => !string.IsNullOrWhiteSpace(p.PerceptualHash))
            .GroupBy(p => p.PerceptualHash[..Math.Min(prefix, p.PerceptualHash.Length)])
            .Where(g => g.Count() > 1)
            .Select((g, i) => new SimilarityGroup(i + 1, "Podobne ujęcia", g.OrderByDescending(p => p.QualityScore).ToArray()))
            .ToArray();

        return groups;
    }

    public static IReadOnlyList<SimilarityGroup> BuildExactDuplicates(IReadOnlyList<PhotoItem> photos)
    {
        return photos
            .Where(p => !string.IsNullOrWhiteSpace(p.Sha256))
            .GroupBy(p => p.Sha256)
            .Where(g => g.Count() > 1)
            .Select((g, i) => new SimilarityGroup(i + 1, "Dokładne duplikaty", g.ToArray()))
            .ToArray();
    }

    public static IReadOnlyList<SimilarityGroup> BuildSimilarPhotos(IReadOnlyList<PhotoItem> photos)
    {
        var embedded = BuildEmbeddingGroups(photos, 0.78, "Podobne zdjęcia").Where(g => g.Photos.Count > 2).ToArray();
        if (embedded.Length > 0)
            return embedded;

        return photos
            .Where(p => !p.IsVideo && !string.IsNullOrWhiteSpace(p.PerceptualHash))
            .GroupBy(p => p.PerceptualHash[..Math.Min(4, p.PerceptualHash.Length)])
            .Where(g => g.Count() > 2)
            .Select((g, i) => new SimilarityGroup(i + 1, "Podobne zdjęcia", g.ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<SimilarityGroup> BuildEmbeddingGroups(IReadOnlyList<PhotoItem> photos, double threshold, string type)
    {
        var candidates = photos.Where(p => !p.IsVideo && p.Embedding is { Length: > 0 }).ToArray();
        var used = new HashSet<PhotoItem>();
        var groups = new List<SimilarityGroup>();

        // ponytail: O(n^2) in-folder scan; replace with vector index when 10k+ AI embeddings becomes slow.
        foreach (var photo in candidates)
        {
            if (!used.Add(photo))
                continue;

            var group = candidates
                .Where(other => !ReferenceEquals(photo, other) && Cosine(photo.Embedding!, other.Embedding!) >= threshold)
                .Append(photo)
                .OrderByDescending(p => p.QualityScore)
                .ToArray();

            if (group.Length < 2)
                continue;

            foreach (var item in group)
                used.Add(item);
            groups.Add(new SimilarityGroup(groups.Count + 1, type, group));
        }

        return groups;
    }

    private static double Cosine(double[] a, double[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }

        return aa == 0 || bb == 0 ? 0 : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}
