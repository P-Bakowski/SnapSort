using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapSort.App.Models;
using SnapSort.App.Services;
using LibVLCSharp.Shared;

namespace SnapSort.App;

public partial class PreviewWindow : Window
{
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;

    public PreviewWindow()
    {
        InitializeComponent();
        Loaded += PreviewLoaded;
        Closed += (_, _) => DisposePlayer();
    }

    private void PreviewLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PhotoItem photo)
            return;

        if (!photo.IsVideo)
        {
            VideoView.Visibility = Visibility.Collapsed;
            FullImage.Source = PhotoLoader.TryLoadBitmap(photo.FullPath, 2200);
            return;
        }

        Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--avcodec-hw=none", "--vout=direct3d9");
        _player = new MediaPlayer(_libVlc);
        _media = new Media(_libVlc, photo.FullPath, FromType.FromPath);
        VideoView.MediaPlayer = _player;
        _player.PositionChanged += PlayerPositionChanged;
        _player.TimeChanged += PlayerTimeChanged;
        _player.EncounteredError += (_, _) => Dispatcher.Invoke(() =>
            AppDialog.Show("Nie udało się odtworzyć tego filmu.", "SnapSort"));
        if (!_player.Play(_media))
            AppDialog.Show("Nie udało się uruchomić tego filmu.", "SnapSort");
    }

    private void PlayVideo(object sender, RoutedEventArgs e) => _player?.Play();
    private void PauseVideo(object sender, RoutedEventArgs e) => _player?.Pause();
    private void SeekVideo(object sender, MouseButtonEventArgs e)
    {
        if (_player is null || sender is not Slider slider || slider.ActualWidth <= 0)
            return;

        var position = Math.Clamp(e.GetPosition(slider).X / slider.ActualWidth, 0, 1);
        slider.Value = position;
        _player.Position = (float)position;
        e.Handled = true;
    }

    private void PlayerPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => Timeline.Value = e.Position);

    private void PlayerTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => TimeLabel.Text = $"{FormatTime(e.Time)} / {FormatTime(_player?.Length ?? 0)}");

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private void DisposePlayer()
    {
        VideoView.MediaPlayer = null;
        _player?.Stop();
        _media?.Dispose();
        _player?.Dispose();
        _libVlc?.Dispose();
    }
}
