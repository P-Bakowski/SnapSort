using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SnapSort.App.Models;

namespace SnapSort.App.Services;

public sealed class PhotoLoader
{
    private readonly PhotoIndex _index = new();
    private readonly ThumbnailCache _thumbnailCache = new();
    private readonly WorkerClient _workerClient = new();
    public event Action<int, int, int, int>? ProgressChanged;
    public event Action<string>? StatusChanged;
    public AppSettings Settings { get; set; } = new();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".mp4", ".mov", ".avi", ".mkv", ".m4v",
        ".wmv", ".webm", ".mpeg", ".mpg", ".3gp"
    };

    public async Task LoadFolderAsync(string folderPath, ObservableCollection<PhotoItem> target, CancellationToken token)
    {
        target.Clear();

        var files = await Task.Run(() =>
        {
            try
            {
                return Directory.EnumerateFiles(folderPath)
                    .Where(IsSupportedFile)
                    .OrderBy(path => path)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }, token);

        StatusChanged?.Invoke($"Wczytywanie folderu 0 / {files.Length}...");
        var analysisQueue = new List<(PhotoItem Item, AnalysisState State)>();
        var loadedCount = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var item = new PhotoItem(file);
            var state = item.IsVideo ? null : _index.GetAnalysisState(item);
            _index.UpsertFile(item);
            _ = LoadThumbnailAsync(item, token);
            if (!item.IsVideo)
            {
                if (state?.Cached is not null)
                    ApplyAnalysis(item, state.Cached, state.OrientationAccepted);
                if (Settings.AutoAnalyze && state?.NeedsAnalysis == true)
                    analysisQueue.Add((item, state));
            }
            target.Add(item);

            loadedCount++;
            if (loadedCount % 25 == 0 || loadedCount == files.Length)
                StatusChanged?.Invoke($"Wczytywanie folderu {loadedCount} / {files.Length}...");
        }

        var photoCount = target.Count(item => !item.IsVideo);
        var readyCount = photoCount - analysisQueue.Count;
        if (!Settings.AutoAnalyze)
        {
            ProgressChanged?.Invoke(readyCount, photoCount, 0, 0);
            StatusChanged?.Invoke("Automatyczna analiza wyłączona");
            return;
        }

        if (analysisQueue.Count == 0)
        {
            ProgressChanged?.Invoke(photoCount, photoCount, 0, 0);
            StatusChanged?.Invoke("");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var failed = 0;
        var completed = 0;
        var analyzed = 0;
        StatusChanged?.Invoke("");
        ProgressChanged?.Invoke(readyCount, photoCount, 0, analysisQueue.Count);
        foreach (var (item, state) in analysisQueue)
        {
            token.ThrowIfCancellationRequested();
            StatusChanged?.Invoke($"Analizowanie {completed + 1} / {analysisQueue.Count}: {item.FileName}");
            if (await AnalyzeAsync(item, state, token))
                analyzed++;
            else
                failed++;
            ProgressChanged?.Invoke(readyCount + analyzed, photoCount, ++completed, analysisQueue.Count);
        }

        stopwatch.Stop();
        AppLog.Info($"Analysis session: Photos={target.Count(p => !p.IsVideo)} CacheHits={target.Count(p => !p.IsVideo) - analysisQueue.Count} Analyzed={analyzed} Failed={failed} Total={stopwatch.Elapsed.TotalSeconds:0.###}s Average={(analysisQueue.Count == 0 ? 0 : stopwatch.Elapsed.TotalMilliseconds / analysisQueue.Count):0.#}ms/photo");
        StatusChanged?.Invoke(failed == 0 ? "" : $"Nie udało się przeanalizować: {failed}.");
    }

    private async Task LoadThumbnailAsync(PhotoItem item, CancellationToken token)
    {
        try
        {
            var (image, duration) = await Task.Run(() => item.IsVideo
                ? _thumbnailCache.GetOrCreateVideo(item.FullPath, 420)
                : (_thumbnailCache.GetOrCreate(item.FullPath, 420), 0L), token);
            if (image is null || token.IsCancellationRequested)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                item.Thumbnail = image;
                item.DurationMilliseconds = duration;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "thumbnail load");
        }
    }

    private async Task<bool> AnalyzeAsync(PhotoItem item, AnalysisState state, CancellationToken token)
    {
        try
        {
            var result = await _workerClient.AnalyzeImageAsync(
                item.FullPath,
                state.NeedsEmbedding,
                state.NeedsBlur,
                state.NeedsOrientation,
                token);
            if (result is null || token.IsCancellationRequested)
                return false;

            if (state.Cached is { } cached)
                result = result with
                {
                    Embedding = state.NeedsEmbedding ? result.Embedding : cached.Embedding,
                    Sharpness = state.NeedsBlur ? result.Sharpness : cached.Sharpness,
                    QualityScore = state.NeedsBlur ? result.QualityScore : cached.QualityScore,
                    Orientation = state.NeedsOrientation ? result.Orientation : cached.Orientation,
                    OrientationConfidence = state.NeedsOrientation ? result.OrientationConfidence : cached.OrientationConfidence,
                    SecondBestOrientationConfidence = state.NeedsOrientation ? result.SecondBestOrientationConfidence : cached.SecondBestOrientationConfidence,
                    SuggestedRotation = state.NeedsOrientation ? result.SuggestedRotation : cached.SuggestedRotation
                };

            _index.SaveAnalysis(result, state.NeedsEmbedding, state.NeedsBlur, state.NeedsOrientation);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                ApplyAnalysis(item, result, state.OrientationAccepted));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "image analysis");
            return false;
        }
    }

    public async Task<PhotoItem> ReloadPhotoAsync(string path, CancellationToken token)
    {
        var item = new PhotoItem(path);
        _index.UpsertFile(item);
        await LoadThumbnailAsync(item, token);
        if (!item.IsVideo)
        {
            var result = await _workerClient.AnalyzeImageAsync(item.FullPath, true, true, true, token);
            if (result is not null)
            {
                _index.SaveAnalysis(result);
                ApplyAnalysis(item, result, false);
            }
        }

        return item;
    }

    public void AcceptOrientation(PhotoItem item)
    {
        _index.AcceptOrientation(item);
        item.OrientationAccepted = true;
    }

    private void ApplyAnalysis(PhotoItem item, AnalysisResult result, bool orientationAccepted)
    {
        item.PerceptualHash = result.PerceptualHash;
        item.Sha256 = result.Sha256;
        item.Embedding = result.Embedding;
        item.SharpnessScore = result.Sharpness;
        item.QualityScore = result.QualityScore;
        item.Width = result.Width;
        item.Height = result.Height;
        item.DateTaken = result.DateTaken;
        item.Orientation = result.Orientation;
        item.OrientationConfidence = result.OrientationConfidence;
        item.SecondBestOrientationConfidence = result.SecondBestOrientationConfidence;
        item.SuggestedRotation = result.SuggestedRotation;
        item.OrientationAccepted = orientationAccepted;
        item.Badge = Settings.DetectBlur && result.Sharpness < 0.35 ? "Rozmazane" : "";
        item.NotifyAnalysisUpdated();
        if (Environment.GetEnvironmentVariable("SnapSort_ORIENTATION_LOG") == "1")
            AppLog.Info($"Orientation File={item.FileName} ExifOrientation={result.Orientation} DisplayDimensions={result.Width}x{result.Height} SuggestedRotation={result.SuggestedRotation} Confidence={result.OrientationConfidence:0.###} IsSideways={item.IsSideways}");
    }

    public static BitmapSource? TryLoadBitmap(string path, int decodePixelWidth)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource image = PhotoDisplayOrientation.ApplyForDisplay(frame, PhotoDisplayOrientation.ReadExifOrientation(frame));
            if (decodePixelWidth > 0)
            {
                var scale = Math.Min(1.0, decodePixelWidth / (double)Math.Max(image.PixelWidth, image.PixelHeight));
                if (scale < 1.0)
                    image = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
            }

            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsSupportedFile(string path) => Extensions.Contains(Path.GetExtension(path));

}
