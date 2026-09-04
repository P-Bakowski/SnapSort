using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SnapSort.App.Models;
using SnapSort.App.Services;

namespace SnapSort.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();
    private readonly PhotoLoader _photoLoader = new();
    private readonly ThumbnailCache _thumbnailCache = new();
    private readonly RotationService _rotationService = new();
    private SafeTrash _safeTrash;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _watchCts;
    private FileSystemWatcher? _folderWatcher;
    private string? _programmaticChangePath;
    private DateTime _ignoreProgrammaticChangeUntil;
    private PhotoItem? _activePhoto;
    private SimilarityGroup? _activeSimilarityGroup;
    private string _currentFolder = "";
    private string _searchText = "";
    private string _currentCollection = "Wszystkie";
    private string _analysisStatus = "";
    private int _analyzedCount;
    private int _analysisTotal;
    private int _queueAnalyzedCount;
    private int _queueAnalysisTotal;
    private int _similarPhotosCount;
    private int _blurryCount;
    private int _sidewaysCount;
    private int _videosCount;
    private HashSet<PhotoItem> _similarPhotoPhotos = new();

    public MainViewModel()
    {
        Settings = _settingsStore.Load();
        _photoLoader.Settings = Settings;
        _safeTrash = new SafeTrash(Settings.TrashFolderName);
        Photos.CollectionChanged += PhotosChanged;
        PhotoView = CollectionViewSource.GetDefaultView(Photos);
        PhotoView.Filter = FilterPhoto;
        _photoLoader.ProgressChanged += (ready, total, queueCompleted, queueTotal) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _analyzedCount = ready;
                _analysisTotal = total;
                _queueAnalyzedCount = queueCompleted;
                _queueAnalysisTotal = queueTotal;
                OnPropertyChanged(nameof(AnalyzedCount));
                OnPropertyChanged(nameof(AnalysisTotal));
                OnPropertyChanged(nameof(QueueAnalyzedCount));
                OnPropertyChanged(nameof(QueueAnalysisTotal));
                OnPropertyChanged(nameof(AnalysisProgressText));
                OnPropertyChanged(nameof(QueueAnalysisText));
            });
        };
        _photoLoader.StatusChanged += status => Application.Current.Dispatcher.Invoke(() => AnalysisStatus = status);

        OpenFolderCommand = new RelayCommand(node => OpenFolder(node as FolderNode));
        MoveSelectedToTrashCommand = new RelayCommand(_ => MoveSelectedToTrash(), _ => Photos.Any(p => p.IsSelected));
        KeepSelectedCommand = new RelayCommand(_ => ClearSelection(), _ => Photos.Any(p => p.IsSelected));
        SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => CanSelectAll);
        UndoCommand = new RelayCommand(_ => UndoLast());
        ShowCollectionCommand = new RelayCommand(collection => ShowCollection(collection?.ToString() ?? "Wszystkie"));
        RotatePhotoCommand = new RelayCommand(photo => RotatePhoto(photo));
        RotateLeftCommand = new RelayCommand(photo => RotatePhoto(photo, 270));
        RotateRightCommand = new RelayCommand(photo => RotatePhoto(photo, 90));
        RotateSelectedCommand = new RelayCommand(_ => RotateSelected(), _ => Photos.Any(p => p.IsSelected && !p.IsVideo));
        AcceptOrientationCommand = new RelayCommand(photo => AcceptOrientation(photo as PhotoItem), photo =>
            photo is PhotoItem { IsSideways: true });
        CleanupCacheCommand = new RelayCommand(_ => AnalysisStatus = $"Usunięto miniatur: {_thumbnailCache.CleanupOlderThan(TimeSpan.FromDays(30))}");
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());

        LoadRoots();
    }

    public ObservableCollection<FolderNode> Folders { get; } = new();
    public ObservableCollection<PhotoItem> Photos { get; } = new();
    public ObservableCollection<SimilarityGroup> SimilarityGroups { get; } = new();
    public ICollectionView PhotoView { get; }
    public AppSettings Settings { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand MoveSelectedToTrashCommand { get; }
    public ICommand KeepSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand ShowCollectionCommand { get; }
    public ICommand RotatePhotoCommand { get; }
    public ICommand RotateLeftCommand { get; }
    public ICommand RotateRightCommand { get; }
    public ICommand RotateSelectedCommand { get; }
    public ICommand AcceptOrientationCommand { get; }
    public ICommand CleanupCacheCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public PhotoItem? ActivePhoto
    {
        get => _activePhoto;
        set => SetField(ref _activePhoto, value);
    }

    public SimilarityGroup? ActiveSimilarityGroup
    {
        get => _activeSimilarityGroup;
        set
        {
            if (SetField(ref _activeSimilarityGroup, value))
            {
                OnPropertyChanged(nameof(IsSimilarityGroupOpen));
                PhotoView.Refresh();
                (SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentFolder
    {
        get => _currentFolder;
        set => SetField(ref _currentFolder, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                PhotoView.Refresh();
        }
    }

    public double ThumbnailSize
    {
        get => Settings.ThumbnailSize;
        set
        {
            if (Math.Abs(Settings.ThumbnailSize - value) < 0.1)
                return;

            Settings.ThumbnailSize = value;
            OnPropertyChanged();
            _settingsStore.Save(Settings);
        }
    }

    public string StatusText => $"Zaznaczono: {Photos.Count(p => p.IsSelected)} zdjęć | Łącznie: {Photos.Count} elementów";
    public string CurrentCollection
    {
        get => _currentCollection;
        set
        {
            if (SetField(ref _currentCollection, value))
            {
                OnPropertyChanged(nameof(IsSimilarityGroupOpen));
                (SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }
    public bool IsSimilarityGroupOpen => CurrentCollection == "Podobne zdjęcia" && ActiveSimilarityGroup is not null;
    private bool CanSelectAll => CurrentCollection != "Podobne zdjęcia" || ActiveSimilarityGroup is not null;

    public string AnalysisStatus
    {
        get => _analysisStatus;
        set => SetField(ref _analysisStatus, value);
    }

    public int AnalyzedCount => _analyzedCount;
    public int AnalysisTotal => _analysisTotal;
    public int QueueAnalyzedCount => _queueAnalyzedCount;
    public int QueueAnalysisTotal => _queueAnalysisTotal;
    public string AnalysisProgressText => AnalysisTotal == 0 ? "" : $"Stan analizy {AnalyzedCount}/{AnalysisTotal}";
    public string QueueAnalysisText => QueueAnalysisTotal == 0 ? "" : $"Nowa analiza {QueueAnalyzedCount}/{QueueAnalysisTotal}";

    public int BlurryCount
    {
        get => _blurryCount;
        set => SetField(ref _blurryCount, value);
    }

    public int SimilarPhotosCount
    {
        get => _similarPhotosCount;
        set => SetField(ref _similarPhotosCount, value);
    }

    public int SidewaysCount
    {
        get => _sidewaysCount;
        set => SetField(ref _sidewaysCount, value);
    }

    public int VideosCount
    {
        get => _videosCount;
        set => SetField(ref _videosCount, value);
    }

    private void PhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PhotoItem item in e.OldItems)
                item.PropertyChanged -= PhotoChanged;

        if (e.NewItems is not null)
            foreach (PhotoItem item in e.NewItems)
                item.PropertyChanged += PhotoChanged;

        if (ActivePhoto is null && Photos.Count > 0)
            ActivePhoto = Photos[0];

        OnPropertyChanged(nameof(StatusText));
    }

    private void PhotoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoItem.IsSelected))
        {
            OnPropertyChanged(nameof(StatusText));
            (MoveSelectedToTrashCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (KeepSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RotateSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        if (e.PropertyName == nameof(PhotoItem.AnalysisRevision))
        {
            RebuildCollections();
            (RotateSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void MoveSelectedToTrash()
    {
        MoveToTrash(Photos.Where(p => p.IsSelected));
    }

    public IReadOnlyList<PhotoItem> MoveToTrash(IEnumerable<PhotoItem> photos)
    {
        var candidates = photos.Where(photo => Photos.Contains(photo)).Distinct().ToArray();
        if (candidates.Length == 0 || !AppDialog.Confirm(
                candidates.Length == 1
                    ? $"Czy przenieść plik „{candidates[0].FileName}” do lokalnego folderu kosza?"
                    : $"Czy przenieść {candidates.Length} zaznaczonych plików do lokalnego folderu kosza?",
                "SnapSort"))
            return Array.Empty<PhotoItem>();

        var moved = _safeTrash.MoveToTrash(candidates);
        foreach (var photo in moved)
            Photos.Remove(photo);
        RebuildCollections();
        return moved;
    }

    private void ClearSelection()
    {
        foreach (var photo in Photos.Where(p => p.IsSelected))
            photo.IsSelected = false;
    }

    private void SelectAll()
    {
        foreach (var photo in PhotoView.Cast<PhotoItem>())
            photo.IsSelected = true;
    }

    private void UndoLast()
    {
        if (_safeTrash.UndoLast() && Directory.Exists(CurrentFolder))
            OpenFolder(new FolderNode(CurrentFolder, Path.GetFileName(CurrentFolder)));
    }

    private void SaveSettings()
    {
        _settingsStore.Save(Settings);
        ThemeManager.Apply(Settings.Theme);
        _safeTrash = new SafeTrash(Settings.TrashFolderName);
        RebuildCollections();
        PhotoView.Refresh();
    }

    public void SetTheme(string theme)
    {
        Settings.Theme = theme;
        _settingsStore.Save(Settings);
        ThemeManager.Apply(theme);
    }

    private void RebuildCollections()
    {
        var similar = SimilarityAnalyzer.BuildSimilarShots(Photos, Settings.SimilaritySensitivity);
        var activeGroupPath = ActiveSimilarityGroup?.Photos.FirstOrDefault()?.FullPath;
        _similarPhotoPhotos = similar.SelectMany(g => g.Photos).ToHashSet();

        SimilarPhotosCount = _similarPhotoPhotos.Count;
        BlurryCount = Settings.DetectBlur ? Photos.Count(p => p.Badge == "Rozmazane") : 0;
        SidewaysCount = Photos.Count(p => p.IsSideways);
        VideosCount = Photos.Count(p => p.IsVideo);

        SimilarityGroups.Clear();
        foreach (var group in similar)
            SimilarityGroups.Add(group with { Type = "Podobne zdjęcia" });
        ActiveSimilarityGroup = activeGroupPath is null
            ? null
            : SimilarityGroups.FirstOrDefault(group => group.Photos.Any(photo => photo.FullPath.Equals(activeGroupPath, StringComparison.OrdinalIgnoreCase)));

        PhotoView.Refresh();
    }

    private void ShowCollection(string collection)
    {
        CurrentCollection = collection;
        ActiveSimilarityGroup = null;
        PhotoView.Refresh();
    }

    private bool FilterPhoto(object item)
    {
        if (item is not PhotoItem photo)
            return false;

        if (!string.IsNullOrWhiteSpace(SearchText) && !photo.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            return false;

        return CurrentCollection switch
        {
            "Podobne zdjęcia" => ActiveSimilarityGroup?.Photos.Contains(photo) ?? _similarPhotoPhotos.Contains(photo),
            "Rozmazane" => photo.Badge == "Rozmazane",
            "Zdjęcia bokiem" => photo.IsSideways,
            "Filmy" => photo.IsVideo,
            _ => true
        };
    }

    private async void RotatePhoto(object? arg, int? forcedDegrees = null)
    {
        if (arg is not PhotoItem photo)
            return;

        try
        {
            var degrees = forcedDegrees ?? (photo.SuggestedRotation is 90 or 270 ? photo.SuggestedRotation : 90);
            AnalysisStatus = "Obracanie zdjęcia...";
            IgnoreWatcherFor(photo.FullPath);
            var result = await _rotationService.RotateAsync(photo.FullPath, degrees, CancellationToken.None);
            if (!result.Success)
            {
                AnalysisStatus = result.Message;
                AppDialog.Show(result.Message, "SnapSort");
                return;
            }

            var refreshed = await _photoLoader.ReloadPhotoAsync(photo.FullPath, CancellationToken.None);
            var current = Photos.FirstOrDefault(item => item.FullPath.Equals(photo.FullPath, StringComparison.OrdinalIgnoreCase));
            var index = current is null ? -1 : Photos.IndexOf(current);
            if (index >= 0)
            {
                Photos[index] = refreshed;
                ActivePhoto = refreshed;
            }
            AnalysisStatus = result.Message;
            RebuildCollections();
        }
        catch (Exception ex)
        {
            AnalysisStatus = $"Nie udało się obrócić zdjęcia: {ex.Message}";
            AppDialog.Show(AnalysisStatus, "SnapSort");
        }
    }

    private async void RotateSelected()
    {
        try
        {
            foreach (var photo in Photos.Where(p => p.IsSelected && !p.IsVideo).ToArray())
            {
                IgnoreWatcherFor(photo.FullPath);
                var degrees = photo.SuggestedRotation is 90 or 180 or 270 ? photo.SuggestedRotation : 90;
                var result = await _rotationService.RotateAsync(photo.FullPath, degrees, CancellationToken.None);
                if (!result.Success)
                {
                    AnalysisStatus = result.Message;
                    continue;
                }

                var index = Photos.IndexOf(photo);
                if (index >= 0)
                    Photos[index] = await _photoLoader.ReloadPhotoAsync(photo.FullPath, CancellationToken.None);
            }

            AnalysisStatus = "Obrót zaznaczonych zakończony.";
            RebuildCollections();
        }
        catch (Exception ex)
        {
            AnalysisStatus = $"Nie udało się obrócić zaznaczonych zdjęć: {ex.Message}";
            AppDialog.Show(AnalysisStatus, "SnapSort");
        }
    }

    private void AcceptOrientation(PhotoItem? photo)
    {
        if (photo is not { IsSideways: true })
            return;

        _photoLoader.AcceptOrientation(photo);
        photo.IsSelected = false;
        AnalysisStatus = $"Zaakceptowano położenie: {photo.FileName}";
        RebuildCollections();
    }

    public async void OpenFolder(FolderNode? node)
    {
        if (node is null || node.IsPlaceholder)
            return;

        node.LoadChildren();
        CurrentFolder = node.Path;
        CurrentCollection = "Wszystkie";
        ActivePhoto = null;
        AnalysisStatus = "";
        _analyzedCount = 0;
        _analysisTotal = 0;
        _queueAnalyzedCount = 0;
        _queueAnalysisTotal = 0;
        OnPropertyChanged(nameof(AnalyzedCount));
        OnPropertyChanged(nameof(AnalysisTotal));
        OnPropertyChanged(nameof(QueueAnalyzedCount));
        OnPropertyChanged(nameof(QueueAnalysisTotal));
        OnPropertyChanged(nameof(AnalysisProgressText));
        OnPropertyChanged(nameof(QueueAnalysisText));
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        WatchFolder(node.Path);

        try
        {
            await _photoLoader.LoadFolderAsync(node.Path, Photos, _loadCts.Token);
            ActivePhoto = Photos.FirstOrDefault();
            RebuildCollections();
            OnPropertyChanged(nameof(StatusText));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WatchFolder(string folder)
    {
        _watchCts?.Cancel();
        _folderWatcher?.Dispose();
        _folderWatcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _folderWatcher.Created += FolderChanged;
        _folderWatcher.Changed += FolderChanged;
        _folderWatcher.Deleted += FolderChanged;
        _folderWatcher.Renamed += FolderChanged;
    }

    private void FolderChanged(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow < _ignoreProgrammaticChangeUntil && IsProgrammaticRotationEvent(e.FullPath))
            return;

        if (!PhotoLoader.IsSupportedFile(e.FullPath))
            return;

        var refresh = new CancellationTokenSource();
        Interlocked.Exchange(ref _watchCts, refresh)?.Cancel();
        _ = RefreshFolderAfterChangeAsync(CurrentFolder, refresh.Token);
    }

    private void IgnoreWatcherFor(string path)
    {
        _programmaticChangePath = path;
        _ignoreProgrammaticChangeUntil = DateTime.UtcNow.AddSeconds(3);
    }

    private bool IsProgrammaticRotationEvent(string path)
    {
        if (_programmaticChangePath is null)
            return false;

        if (path.Equals(_programmaticChangePath, StringComparison.OrdinalIgnoreCase))
            return true;

        var rotatedName = Path.GetFileName(_programmaticChangePath);
        return Path.GetFileName(path).StartsWith($".{rotatedName}.", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshFolderAfterChangeAsync(string folder, CancellationToken token)
    {
        try
        {
            await Task.Delay(1200, token);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (CurrentFolder.Equals(folder, StringComparison.OrdinalIgnoreCase))
                {
                    var collection = CurrentCollection;
                    var groupPath = ActiveSimilarityGroup?.Photos.FirstOrDefault()?.FullPath;
                    OpenFolder(new FolderNode(folder, Path.GetFileName(folder)));
                    ShowCollection(collection);
                    if (groupPath is not null)
                        ActiveSimilarityGroup = SimilarityGroups.FirstOrDefault(group => group.Photos.Any(photo => photo.FullPath.Equals(groupPath, StringComparison.OrdinalIgnoreCase)));
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LoadRoots()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Directory.Exists(pictures))
            Folders.Add(new FolderNode(pictures, "Obrazy"));

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            Folders.Add(new FolderNode(drive.RootDirectory.FullName, drive.Name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
