using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace SnapSort.App.Services;

public sealed class ThumbnailCache
{
    private const int RenderVersion = 3;
    // ponytail: serial extraction avoids opening many video decoders at once; use a small semaphore if this becomes a bottleneck.
    private static readonly object VideoExtractionLock = new();

    public ThumbnailCache()
    {
        Directory.CreateDirectory(AppPaths.ThumbnailDir);
    }

    public BitmapSource? GetOrCreate(string originalPath, int width)
    {
        var cachePath = CachePath(originalPath, width);
        if (!File.Exists(cachePath))
            SaveJpeg(originalPath, cachePath, width);

        return PhotoLoader.TryLoadBitmap(cachePath, width);
    }

    public (BitmapSource? Image, long DurationMilliseconds) GetOrCreateVideo(string originalPath, int width)
    {
        var cachePath = CachePath(originalPath, width);
        var durationPath = cachePath + ".duration";
        lock (VideoExtractionLock)
        {
            if (!File.Exists(cachePath) || !long.TryParse(ReadText(durationPath), out _))
                SaveVideoFrame(originalPath, cachePath, durationPath, width);
        }

        long.TryParse(ReadText(durationPath), out var duration);
        return (PhotoLoader.TryLoadBitmap(cachePath, width), duration);
    }

    private static string CachePath(string originalPath, int width)
    {
        var info = new FileInfo(originalPath);
        var key = $"{RenderVersion}|{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{width}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(AppPaths.ThumbnailDir, $"{hash}.jpg");
    }

    private static void SaveJpeg(string sourcePath, string cachePath, int width)
    {
        var image = PhotoLoader.TryLoadBitmap(sourcePath, width);
        if (image is null)
            return;

        var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(cachePath);
        encoder.Save(stream);
    }

    private static void SaveVideoFrame(string sourcePath, string cachePath, string durationPath, int width)
    {
        Core.Initialize();
        using var libVlc = new LibVLC("--no-audio", "--no-video-title-show", "--avcodec-hw=none");
        using var media = new Media(libVlc, sourcePath, FromType.FromPath);
        media.Parse(MediaParseOptions.ParseLocal, 5000, CancellationToken.None).GetAwaiter().GetResult();

        var videoTrack = media.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Video);
        var sourceWidth = videoTrack.Data.Video.Width;
        var sourceHeight = videoTrack.Data.Video.Height;
        if (sourceWidth == 0 || sourceHeight == 0)
            return;

        var scale = width / (double)Math.Max(sourceWidth, sourceHeight);
        var outputWidth = Math.Max(1u, (uint)Math.Round(sourceWidth * scale));
        var outputHeight = Math.Max(1u, (uint)Math.Round(sourceHeight * scale));
        var pitch = Align(outputWidth * 4);
        var lines = Align(outputHeight);
        var buffer = Marshal.AllocHGlobal(checked((int)(pitch * lines)));
        var frameLock = new object();
        byte[]? firstFrame = null;
        byte[]? selectedFrame = null;
        var duration = Math.Max(0, media.Duration);
        var target = duration > 0 ? Math.Min(1500, Math.Max(0, duration / 3)) : 1000;

        try
        {
            using var player = new MediaPlayer(libVlc);
            using var playing = new ManualResetEventSlim();
            using var frameReady = new ManualResetEventSlim();
            MediaPlayer.LibVLCVideoLockCb lockCallback = (_, planes) =>
            {
                Marshal.WriteIntPtr(planes, buffer);
                return buffer;
            };
            MediaPlayer.LibVLCVideoDisplayCb displayCallback = (_, _) =>
            {
                var frame = new byte[checked((int)(pitch * outputHeight))];
                Marshal.Copy(buffer, frame, 0, frame.Length);
                lock (frameLock)
                {
                    firstFrame ??= frame;
                    if (player.Time >= target)
                    {
                        selectedFrame = frame;
                        frameReady.Set();
                    }
                }
            };

            player.SetVideoFormat("RV32", outputWidth, outputHeight, pitch);
            player.SetVideoCallbacks(lockCallback, null, displayCallback);
            player.Playing += (_, _) => playing.Set();
            if (!player.Play(media) || !playing.Wait(TimeSpan.FromSeconds(6)))
                return;

            duration = Math.Max(duration, player.Length);
            target = duration > 0 ? Math.Min(1500, Math.Max(0, duration / 3)) : 1000;
            frameReady.Wait(TimeSpan.FromSeconds(6));
            player.Stop();

            byte[]? frame;
            lock (frameLock)
                frame = selectedFrame ?? firstFrame;
            if (frame is null)
                return;

            var image = BitmapSource.Create((int)outputWidth, (int)outputHeight, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, frame, (int)pitch);
            image.Freeze();
            var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(cachePath))
                encoder.Save(stream);
            File.WriteAllText(durationPath, duration.ToString(System.Globalization.CultureInfo.InvariantCulture));

            GC.KeepAlive(lockCallback);
            GC.KeepAlive(displayCallback);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint Align(uint size) => size % 32 == 0 ? size : (size / 32 + 1) * 32;

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    public int CleanupOlderThan(TimeSpan age)
    {
        var cutoff = DateTime.UtcNow - age;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(AppPaths.ThumbnailDir, "*.jpg"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff)
                    continue;

                File.Delete(file);
                File.Delete(file + ".duration");
                removed++;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "thumbnail cleanup");
            }
        }

        return removed;
    }
}
