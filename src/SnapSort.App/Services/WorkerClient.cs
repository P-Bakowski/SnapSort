using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SnapSort.App.Models;

namespace SnapSort.App.Services;

public sealed class WorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;

    public async Task<AnalysisResult?> AnalyzeImageAsync(
        string path,
        bool embedding,
        bool blur,
        bool orientation,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            _process ??= StartWorker();
            return _process is null
                ? await FallbackAsync(path, token)
                : await AskWorkerAsync(_process, path, embedding, blur, orientation, token);
        }
        catch
        {
            _process?.Dispose();
            _process = null;
            return await FallbackAsync(path, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Process? StartWorker()
    {
        var root = AppContext.BaseDirectory;
        var script = FindUp(root, Path.Combine("python", "PhotoAnalysis.Worker", "main.py"));
        var exe = FindUp(root, Path.Combine("python", "PhotoAnalysis.Worker", "PhotoAnalysis.Worker.exe"))
            ?? FindUp(root, Path.Combine("python", "PhotoAnalysis.Worker", "dist", "PhotoAnalysis.Worker", "PhotoAnalysis.Worker.exe"))
            ?? FindUp(root, Path.Combine("python", "PhotoAnalysis.Worker", "dist", "PhotoAnalysis.Worker.exe"));
        if (exe is null && script is null)
            return null;

        var start = new ProcessStartInfo
        {
            FileName = exe ?? "python",
            Arguments = exe is null ? $"\"{script}\"" : "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(start);
        process?.BeginErrorReadLine();
        return process;
    }

    private static async Task<AnalysisResult?> AskWorkerAsync(
        Process process,
        string imagePath,
        bool embedding,
        bool blur,
        bool orientation,
        CancellationToken token)
    {
        var request = JsonSerializer.Serialize(new
        {
            action = "analyze_photo",
            path = imagePath,
            features = new { embedding, blur, orientation }
        });
        await process.StandardInput.WriteLineAsync(request);
        await process.StandardInput.FlushAsync(token);

        var line = await process.StandardOutput.ReadLineAsync(token);
        if (line is null)
            return null;

        var response = JsonSerializer.Deserialize<WorkerResponse>(line, JsonOptions);
        return response?.Success == true
            ? new AnalysisResult(imagePath, response.Sha256, response.PerceptualHash, response.Sharpness, response.QualityScore, response.Width, response.Height, response.DateTaken, response.Embedding, response.Orientation, response.OrientationConfidence, response.SecondBestOrientationConfidence, response.SuggestedRotation)
            : null;
    }

    private static async Task<AnalysisResult> FallbackAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        return new AnalysisResult(path, sha, sha[..16], 0, 0, 0, 0, null, null, 1, 0, 0, 0);
    }

    private static string? FindUp(string start, string relative)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private sealed record WorkerResponse(
        bool Success,
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
}
