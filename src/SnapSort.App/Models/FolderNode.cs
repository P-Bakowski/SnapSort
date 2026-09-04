using System.Collections.ObjectModel;
using System.IO;

namespace SnapSort.App.Models;

public sealed class FolderNode
{
    private readonly SynchronizationContext? _ownerContext = SynchronizationContext.Current;
    private FileSystemWatcher? _watcher;

    public FolderNode(string path, string name)
    {
        Path = path;
        Name = name;
        Children.Add(new FolderNode());
    }

    private FolderNode()
    {
        Path = "";
        Name = "";
    }

    public string Path { get; }
    public string Name { get; }
    public ObservableCollection<FolderNode> Children { get; } = new();
    public bool IsPlaceholder => string.IsNullOrEmpty(Path);

    public void LoadChildren()
    {
        if (IsPlaceholder || Children.Count != 1 || !Children[0].IsPlaceholder)
            return;

        RefreshChildren();

        try
        {
            _watcher = new FileSystemWatcher(Path)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            _watcher.Created += ChildrenChanged;
            _watcher.Deleted += ChildrenChanged;
            _watcher.Renamed += ChildrenChanged;
        }
        catch
        {
            // Some system folders deny access. The tree should keep working.
        }
    }

    private void ChildrenChanged(object sender, FileSystemEventArgs e)
    {
        if (_ownerContext is null)
            RefreshChildren();
        else
            _ownerContext.Post(_ => RefreshChildren(), null);
    }

    private void RefreshChildren()
    {
        string[] directories;
        try
        {
            directories = Directory.EnumerateDirectories(Path)
                .Where(dir => (File.GetAttributes(dir) & FileAttributes.Hidden) == 0)
                .OrderBy(System.IO.Path.GetFileName)
                .ToArray();
        }
        catch
        {
            return;
        }

        // ponytail: linear scans preserve expanded nodes; use a path map only for folders with thousands of direct children.
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (directories.Contains(Children[i].Path, StringComparer.OrdinalIgnoreCase))
                continue;

            Children[i]._watcher?.Dispose();
            Children.RemoveAt(i);
        }

        for (var targetIndex = 0; targetIndex < directories.Length; targetIndex++)
        {
            var existingIndex = Children.ToList().FindIndex(node => node.Path.Equals(directories[targetIndex], StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
                Children.Insert(targetIndex, new FolderNode(directories[targetIndex], System.IO.Path.GetFileName(directories[targetIndex])));
            else if (existingIndex != targetIndex)
                Children.Move(existingIndex, targetIndex);
        }
    }
}
