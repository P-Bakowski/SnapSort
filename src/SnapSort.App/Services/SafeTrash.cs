using System.IO;
using SnapSort.App.Models;

namespace SnapSort.App.Services;

public sealed class SafeTrash
{
    private readonly PhotoIndex _index = new();

    public SafeTrash(string? _ = null) { }

    public IReadOnlyList<PhotoItem> MoveToTrash(IEnumerable<PhotoItem> photos)
    {
        var moved = new List<PhotoItem>();
        foreach (var photo in photos.ToArray())
        {
            if (!File.Exists(photo.FullPath))
                continue;

            var folder = Path.GetDirectoryName(photo.FullPath);
            if (folder is null)
                continue;

            var folderName = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(folderName))
                folderName = Path.GetPathRoot(folder)?.TrimEnd(Path.DirectorySeparatorChar, Path.VolumeSeparatorChar) ?? "Folder";
            var trash = Path.Combine(folder, $"{folderName}_Kosz");
            Directory.CreateDirectory(trash);
            var destination = UniquePath(Path.Combine(trash, photo.FileName));
            File.Move(photo.FullPath, destination);
            _index.AddHistory("MoveToTrash", photo.FullPath, destination);
            moved.Add(photo);
        }

        return moved;
    }

    public bool UndoLast()
    {
        var move = _index.LastUndoableMove();
        if (move is null || !File.Exists(move.Value.Destination))
            return false;

        var destinationFolder = Path.GetDirectoryName(move.Value.Source);
        if (destinationFolder is null || !Directory.Exists(destinationFolder))
            return false;

        var restorePath = File.Exists(move.Value.Source) ? UniquePath(move.Value.Source) : move.Value.Source;
        File.Move(move.Value.Destination, restorePath);
        _index.MarkUndone(move.Value.Id);
        return true;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
