using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using SnapSort.App.Models;
using SnapSort.App.Services;
using SnapSort.App.ViewModels;

namespace SnapSort.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _hoverTimer;
    private PhotoItem? _hoveredPhoto;
    private FrameworkElement? _hoveredTile;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        StateChanged += (_, _) => Chrome.ResizeBorderThickness = WindowState == WindowState.Maximized
            ? new Thickness(0)
            : new Thickness(6);

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hoverTimer.Tick += async (_, _) =>
        {
            _hoverTimer.Stop();
            var photo = _hoveredPhoto;
            var tile = _hoveredTile;
            if (photo is null || tile?.IsMouseOver != true)
                return;

            var image = await Task.Run(() => PhotoLoader.TryLoadBitmap(photo.FullPath, 760));
            if (_hoveredPhoto != photo || tile.IsMouseOver != true)
                return;

            PreviewImage.Source = image;
            PreviewName.Text = photo.FileName;
            PreviewMeta.Text = $"{photo.SizeLabel}   {photo.ModifiedAt:dd.MM.yyyy HH:mm}";
            QuickPreviewPopup.PlacementTarget = tile;
            QuickPreviewPopup.IsOpen = true;
        };
    }

    private void FolderExpanded(object sender, RoutedEventArgs e)
    {
        if (((TreeViewItem)e.OriginalSource).DataContext is FolderNode node)
            node.LoadChildren();
    }

    private void FolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel && e.NewValue is FolderNode node)
            viewModel.OpenFolder(node);
    }

    private void PhotoMouseEnter(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PhotoItem photo)
            return;

        _hoveredPhoto = photo;
        _hoveredTile = (FrameworkElement)sender;
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void PhotoMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        QuickPreviewPopup.IsOpen = false;
        _hoveredPhoto = null;
        _hoveredTile = null;
    }

    private void PhotoMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
            return;

        if (((FrameworkElement)sender).DataContext is not PhotoItem photo)
            return;

        OpenPreview(photo);
    }

    private void OpenVideo(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PhotoItem { IsVideo: true } video)
            OpenPreview(video);
    }

    private void OpenPreview(PhotoItem photo) =>
        new PreviewWindow { Owner = this, DataContext = photo }.ShowDialog();

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this, DataContext = DataContext }.ShowDialog();
    }

    private void OpenAbout(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void MovePhotoToTrash(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is MenuItem { DataContext: PhotoItem photo })
            viewModel.MoveToTrash([photo]);
    }

    private void OpenCompare(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var photos = viewModel.ActiveSimilarityGroup?.Photos.ToArray()
            ?? viewModel.Photos.Where(p => p.IsSelected).ToArray();
        if (photos.Length == 0)
            return;

        new CompareWindow { Owner = this, DataContext = photos }.ShowDialog();
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SetTheme(ThemeManager.IsDark ? "Jasny" : "Ciemny");
    }

    private void SortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not ComboBox { SelectedIndex: var index })
            return;

        viewModel.PhotoView.SortDescriptions.Clear();
        viewModel.PhotoView.SortDescriptions.Add(index switch
        {
            1 => new SortDescription(nameof(PhotoItem.FileName), ListSortDirection.Ascending),
            2 => new SortDescription(nameof(PhotoItem.FileSize), ListSortDirection.Descending),
            _ => new SortDescription(nameof(PhotoItem.ModifiedAt), ListSortDirection.Descending)
        });
    }

    private void CloseSimilarityGroup(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ActiveSimilarityGroup = null;
    }

    private void DecreaseThumbnailSize(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ThumbnailSize = Math.Max(140, viewModel.ThumbnailSize - 20);
    }

    private void IncreaseThumbnailSize(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ThumbnailSize = Math.Min(280, viewModel.ThumbnailSize + 20);
    }
}
