using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SnapSort.App.Models;

public sealed class PhotoItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFavorite;
    private ImageSource? _thumbnail;
    private string _badge = "";
    private int _qualityScore;
    private double _sharpnessScore;
    private string _perceptualHash = "";
    private string _sha256 = "";
    private double[]? _embedding;
    private int _width;
    private int _height;
    private DateTime? _dateTaken;
    private int _orientation = 1;
    private double _orientationConfidence;
    private double _secondBestOrientationConfidence;
    private int _suggestedRotation;
    private int _analysisRevision;
    private bool _orientationAccepted;
    private long _durationMilliseconds;

    public PhotoItem(string path)
    {
        FullPath = path;
        FileName = Path.GetFileName(path);
        IsVideo = IsVideoPath(path);
        var info = new FileInfo(path);
        FileSize = info.Exists ? info.Length : 0;
        ModifiedAt = info.Exists ? info.LastWriteTime : DateTime.MinValue;
    }

    public string FullPath { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public DateTime ModifiedAt { get; }
    public DateTime DisplayDate => DateTaken ?? ModifiedAt;
    public bool IsVideo { get; }
    public long DurationMilliseconds
    {
        get => _durationMilliseconds;
        set
        {
            if (SetField(ref _durationMilliseconds, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationLabel)));
        }
    }
    public string DurationLabel
    {
        get
        {
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, DurationMilliseconds));
            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss")
                : duration.ToString(@"mm\:ss");
        }
    }
    public string SizeLabel => FileSize < 1024 * 1024 ? $"{FileSize / 1024.0:0.#} KB" : $"{FileSize / 1024.0 / 1024.0:0.#} MB";
    public bool IsSideways => !IsVideo && !OrientationAccepted
        && OrientationConfidence >= 0.55
        && OrientationConfidence - SecondBestOrientationConfidence >= 0.20
        && (Width > Height && SuggestedRotation is 90 or 270
            || Height >= Width && (SuggestedRotation == 180
                || SuggestedRotation == 0 && SecondBestOrientationConfidence >= 0.30));

    public bool OrientationAccepted
    {
        get => _orientationAccepted;
        set
        {
            if (SetField(ref _orientationAccepted, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSideways)));
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string Badge
    {
        get => _badge;
        set => SetField(ref _badge, value);
    }

    public int QualityScore
    {
        get => _qualityScore;
        set => SetField(ref _qualityScore, value);
    }

    public double SharpnessScore
    {
        get => _sharpnessScore;
        set => SetField(ref _sharpnessScore, value);
    }

    public string PerceptualHash
    {
        get => _perceptualHash;
        set => SetField(ref _perceptualHash, value);
    }

    public string Sha256
    {
        get => _sha256;
        set => SetField(ref _sha256, value);
    }

    public double[]? Embedding
    {
        get => _embedding;
        set => SetField(ref _embedding, value);
    }

    public int Width
    {
        get => _width;
        set
        {
            if (SetField(ref _width, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSideways)));
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            if (SetField(ref _height, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSideways)));
        }
    }

    public DateTime? DateTaken
    {
        get => _dateTaken;
        set
        {
            if (SetField(ref _dateTaken, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayDate)));
        }
    }

    public int Orientation
    {
        get => _orientation;
        set => SetField(ref _orientation, value);
    }

    public double OrientationConfidence
    {
        get => _orientationConfidence;
        set
        {
            if (_orientationConfidence == value)
                return;
            SetField(ref _orientationConfidence, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSideways)));
        }
    }

    public double SecondBestOrientationConfidence
    {
        get => _secondBestOrientationConfidence;
        set
        {
            if (_secondBestOrientationConfidence == value)
                return;
            SetField(ref _secondBestOrientationConfidence, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSideways)));
        }
    }

    public int SuggestedRotation
    {
        get => _suggestedRotation;
        set
        {
            if (_suggestedRotation == value)
                return;

            _suggestedRotation = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SuggestedRotation)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSideways)));
        }
    }

    public int AnalysisRevision => _analysisRevision;

    public void NotifyAnalysisUpdated()
    {
        _analysisRevision++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnalysisRevision)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private static bool IsVideoPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".avi", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".3gp", StringComparison.OrdinalIgnoreCase);
    }
}
