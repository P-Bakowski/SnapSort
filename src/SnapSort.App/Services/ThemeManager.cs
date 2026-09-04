using Microsoft.Win32;
using System.Windows;

namespace SnapSort.App.Services;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(string theme)
    {
        var useDark = theme == "Ciemny" || theme == "Systemowy" && WindowsUsesDarkTheme();
        IsDark = useDark;
        var source = new Uri($"Themes/{(useDark ? "DarkTheme" : "LightTheme")}.xaml", UriKind.Relative);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        var replacement = new ResourceDictionary { Source = source };
        if (current is null)
            dictionaries.Insert(0, replacement);
        else
            dictionaries[dictionaries.IndexOf(current)] = replacement;
    }

    private static bool WindowsUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
