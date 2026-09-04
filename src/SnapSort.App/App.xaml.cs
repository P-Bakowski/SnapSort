using System.Windows;
using SnapSort.App.Services;

namespace SnapSort.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Apply(new SettingsStore().Load().Theme);
        base.OnStartup(e);
    }
}
