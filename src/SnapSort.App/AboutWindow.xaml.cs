using System.Reflection;
using System.Windows;

namespace SnapSort.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 1);
        VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
    }
}
