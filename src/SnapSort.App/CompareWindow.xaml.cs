using System.Windows;
using System.Windows.Controls;
using SnapSort.App.Models;
using SnapSort.App.ViewModels;

namespace SnapSort.App;

public partial class CompareWindow : Window
{
    public CompareWindow()
    {
        InitializeComponent();
    }

    private void MovePhotoToTrash(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PhotoItem photo }
            || Owner?.DataContext is not MainViewModel viewModel)
            return;

        var moved = viewModel.MoveToTrash([photo]);
        if (moved.Count == 0)
            return;

        var remaining = ((IEnumerable<PhotoItem>)DataContext).Except(moved).ToArray();
        if (remaining.Length == 0)
            Close();
        else
            DataContext = remaining;
    }
}
