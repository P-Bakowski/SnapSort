using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapSort.App.Models;
using SnapSort.App.Services;
using LibVLCSharp.Shared;

if (args.Contains("--window-chrome"))
{
    WindowChromeChecks.Run();
    return;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static string SaveJpegWithOrientation(string root, int orientation)
{
    var path = Path.Combine(root, $"orientation-{orientation}.jpg");
    var pixels = new byte[40 * 20 * 4];
    for (var i = 0; i < pixels.Length; i += 4)
    {
        pixels[i] = 30;
        pixels[i + 1] = 80;
        pixels[i + 2] = 180;
        pixels[i + 3] = 255;
    }

    var bitmap = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 40 * 4);
    var metadata = new BitmapMetadata("jpg");
    metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
    var encoder = new JpegBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
    using var stream = File.Create(path);
    encoder.Save(stream);
    return path;
}

static byte[] ReadJpegScan(string path)
{
    var bytes = File.ReadAllBytes(path);
    for (var i = 0; i < bytes.Length - 1; i++)
        if (bytes[i] == 0xFF && bytes[i + 1] == 0xDA)
            return bytes[i..];
    throw new Exception("JPEG scan marker not found");
}

var root = Path.Combine(Path.GetTempPath(), "SnapSort.SelfTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var a = Path.Combine(root, "a.jpg");
    var b = Path.Combine(root, "b.jpg");
    File.WriteAllText(a, "a");
    File.WriteAllText(b, "b");

    var watchedFolder = Path.Combine(root, "watched");
    Directory.CreateDirectory(watchedFolder);
    var folderNode = new FolderNode(watchedFolder, "watched");
    folderNode.LoadChildren();
    Directory.CreateDirectory(Path.Combine(watchedFolder, "new-folder"));
    for (var i = 0; i < 40 && !folderNode.Children.Any(child => child.Name == "new-folder"); i++)
        await Task.Delay(50);
    Assert(folderNode.Children.Any(child => child.Name == "new-folder"), "expanded folder tree should update automatically");

    var safeTrash = new SafeTrash();
    var moved = safeTrash.MoveToTrash([new PhotoItem(a)]);
    Assert(moved.Count == 1, "trash should move one file");
    Assert(!File.Exists(a), "source should disappear after trash move");
    Assert(Directory.Exists(Path.Combine(root, $"{Path.GetFileName(root)}_Kosz")), "folder-specific trash should exist");
    Assert(safeTrash.UndoLast(), "undo should restore file");
    Assert(File.Exists(a), "source should exist after undo");

    var p1 = new PhotoItem(a) { Sha256 = "same", PerceptualHash = "ABCDEF0000000000", QualityScore = 50 };
    var p2 = new PhotoItem(b) { Sha256 = "same", PerceptualHash = "ABCDEF0011111111", QualityScore = 90 };
    var p3 = new PhotoItem(Path.Combine(root, "c.jpg")) { Sha256 = "other", PerceptualHash = "ABCD991111111111", QualityScore = 70 };

    Assert(SimilarityAnalyzer.BuildExactDuplicates([p1, p2, p3]).Count == 1, "duplicates should group equal SHA-256");
    Assert(SimilarityAnalyzer.BuildSimilarShots([p1, p2]).Count == 1, "similar shots should group close pHash prefix");
    Assert(SimilarityAnalyzer.BuildSimilarShots([p1, p2], "Ścisła").Count == 0, "strict sensitivity should split weaker matches");
    Assert(SimilarityAnalyzer.BuildSimilarPhotos([p1, p2, p3]).Count == 1, "similar photos should group looser pHash prefix");

    p1.Embedding = [1, 0, 0];
    p2.Embedding = [0.95, 0.05, 0];
    p3.Embedding = [0, 1, 0];
    Assert(SimilarityAnalyzer.BuildSimilarShots([p1, p2, p3]).Count == 1, "AI embeddings should group by cosine similarity");
    Assert(new PhotoItem(Path.Combine(root, "clip.mkv")).IsVideo, "mkv should be treated as video");
    Assert(new PhotoItem(Path.Combine(root, "clip.avi")).IsVideo, "avi should be treated as video");
    Assert(new PhotoItem(Path.Combine(root, "clip.webm")).IsVideo, "webm should be treated as video");
    Assert(new PhotoItem(Path.Combine(root, "clip.mp4")) { DurationMilliseconds = 252000 }.DurationLabel == "04:12", "short video duration should use mm:ss");
    Assert(new PhotoItem(Path.Combine(root, "clip.mp4")) { DurationMilliseconds = 5025000 }.DurationLabel == "01:23:45", "long video duration should use hh:mm:ss");
    Core.Initialize();
    using (var libVlc = new LibVLC("--no-video-title-show", "--avcodec-hw=none", "--vout=direct3d9"))
    {
        Assert(libVlc is not null, "bundled LibVLC should initialize without a separate VLC installation");
        if (args.Length == 1 && File.Exists(args[0]))
        {
            using var player = new LibVLCSharp.Shared.MediaPlayer(libVlc!);
            using var media = new Media(libVlc!, new Uri(args[0]));
            Assert(player.Play(media), "video playback should start");
            await Task.Delay(2500);
            Assert(player.Time > 0, "video playback timeline should advance");
            player.Stop();

            var videoThumbnail = new ThumbnailCache().GetOrCreateVideo(args[0], 420);
            Assert(videoThumbnail.Image is not null, "video thumbnail should be generated from a real frame");
            Assert(videoThumbnail.DurationMilliseconds > 0, "video duration should be cached with its thumbnail");
        }
    }

    var index = new PhotoIndex();
    index.UpsertFile(p2);
    var savedAnalysis = new AnalysisResult(p2.FullPath, "sha", "phash", 0.8, 90, 120, 80, DateTime.UtcNow, [1, 0], 6, 1, 0.01, 90);
    index.SaveAnalysis(savedAnalysis, embeddingAnalyzed: true, blurAnalyzed: true, orientationAnalyzed: false);
    var partialState = index.GetAnalysisState(p2);
    Assert(!partialState.NeedsEmbedding && !partialState.NeedsBlur && partialState.NeedsOrientation, "orientation model update should preserve embedding and blur cache");
    index.SaveAnalysis(savedAnalysis);
    Assert(index.TryGetCurrentAnalysis(p2)?.SuggestedRotation == 90, "unchanged photo analysis should come from SQLite");
    p2.Orientation = 6;
    p2.OrientationConfidence = 1;
    p2.SecondBestOrientationConfidence = 0;
    p2.SuggestedRotation = 0;
    Assert(!p2.IsSideways, "EXIF-rendered photo should not be marked sideways");
    var uprightPortrait = new PhotoItem(Path.Combine(root, "portrait.jpg")) { Width = 3000, Height = 4000, Orientation = 1, OrientationConfidence = 0, SuggestedRotation = 0 };
    Assert(!uprightPortrait.IsSideways, "upright portrait should not be marked sideways");
    var sidewaysLandscape = new PhotoItem(Path.Combine(root, "sideways.jpg")) { Width = 4000, Height = 3000, Orientation = 6, OrientationConfidence = 0.99, SecondBestOrientationConfidence = 0.01, SuggestedRotation = 90 };
    Assert(sidewaysLandscape.IsSideways, "landscape photo with strong rotate signal should be marked sideways");
    var berlinSideways = new PhotoItem(Path.Combine(root, "20260710_164719.jpg")) { Width = 4000, Height = 3000, Orientation = 1, OrientationConfidence = 0.633312, SecondBestOrientationConfidence = 0.32067, SuggestedRotation = 90 };
    Assert(berlinSideways.IsSideways, "20260710_164719 model result should be marked sideways");
    var upsideDownPortrait = new PhotoItem(Path.Combine(root, "upside-down.jpg")) { Width = 3000, Height = 4000, Orientation = 1, OrientationConfidence = 0.95, SecondBestOrientationConfidence = 0.02, SuggestedRotation = 180 };
    Assert(upsideDownPortrait.IsSideways, "upside-down portrait should stay in the orientation collection");
    var ambiguousUpsideDownPortrait = new PhotoItem(Path.Combine(root, "20260710_164719-rotated.jpg")) { Width = 3000, Height = 4000, Orientation = 6, OrientationConfidence = 0.658207, SecondBestOrientationConfidence = 0.31827, SuggestedRotation = 0 };
    Assert(ambiguousUpsideDownPortrait.IsSideways, "ambiguous 20260710_164719 result should stay in the orientation collection");
    var portraitFalsePositive = new PhotoItem(Path.Combine(root, "portrait-false-positive.jpg")) { Width = 3000, Height = 4000, Orientation = 1, OrientationConfidence = 0.89, SecondBestOrientationConfidence = 0.05, SuggestedRotation = 270 };
    Assert(!portraitFalsePositive.IsSideways, "upright portrait must not be marked sideways even with a strong rotate signal");
    index.AcceptOrientation(p2);
    Assert(index.GetAnalysisState(new PhotoItem(b)).OrientationAccepted, "accepted orientation should persist for the unchanged file");
    sidewaysLandscape.OrientationAccepted = true;
    Assert(!sidewaysLandscape.IsSideways, "accepted photo should leave the sideways collection");
    File.AppendAllText(b, "changed");
    Assert(!index.GetAnalysisState(new PhotoItem(b)).OrientationAccepted, "file modification should invalidate accepted orientation");
    Assert(index.TryGetCurrentAnalysis(new PhotoItem(b)) is null, "changed fingerprint should invalidate cached analysis");

    var settingsStore = new SettingsStore();
    var settings = settingsStore.Load();
    settings.TrashFolderName = "_Kosz";
    settings.SimilaritySensitivity = "Luźna";
    settingsStore.Save(settings);
    Assert(settingsStore.Load().SimilaritySensitivity == "Luźna", "settings should roundtrip");

    var cache = new ThumbnailCache();
    var old = Path.Combine(AppPaths.ThumbnailDir, $"old-{Guid.NewGuid():N}.jpg");
    File.WriteAllText(old, "x");
    File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));
    Assert(cache.CleanupOlderThan(TimeSpan.FromDays(30)) >= 1, "thumbnail cleanup should remove old files");

    foreach (var orientation in Enumerable.Range(1, 8))
        Assert(PhotoDisplayOrientation.GetDisplayOrientation(orientation).ExifOrientation == orientation, $"EXIF orientation {orientation} should be mapped");

    Assert(PhotoLoader.TryLoadBitmap(SaveJpegWithOrientation(root, 1), 0) is { PixelWidth: 40, PixelHeight: 20 }, "EXIF 1 should keep dimensions");
    Assert(PhotoLoader.TryLoadBitmap(SaveJpegWithOrientation(root, 3), 0) is { PixelWidth: 40, PixelHeight: 20 }, "EXIF 3 should keep dimensions");
    Assert(PhotoLoader.TryLoadBitmap(SaveJpegWithOrientation(root, 6), 0) is { PixelWidth: 20, PixelHeight: 40 }, "EXIF 6 should swap dimensions");
    Assert(PhotoLoader.TryLoadBitmap(SaveJpegWithOrientation(root, 8), 0) is { PixelWidth: 20, PixelHeight: 40 }, "EXIF 8 should swap dimensions");
    var metadataRotation = SaveJpegWithOrientation(root, 1);
    var scanBeforeRotation = ReadJpegScan(metadataRotation);
    var rotationResult = await new RotationService().RotateAsync(metadataRotation, 90, CancellationToken.None);
    Assert(rotationResult.Success, "JPEG metadata rotation should succeed without external tools");
    Assert(PhotoLoader.TryLoadBitmap(metadataRotation, 0) is { PixelWidth: 20, PixelHeight: 40 }, "rotated JPEG should display vertically");
    Assert(scanBeforeRotation.SequenceEqual(ReadJpegScan(metadataRotation)), "JPEG compressed pixels must stay byte-identical after rotation");

    Console.WriteLine("Self-tests passed.");
}
finally
{
    Directory.Delete(root, true);
}
