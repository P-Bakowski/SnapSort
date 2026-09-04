using System.Windows;

namespace SnapSort.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
