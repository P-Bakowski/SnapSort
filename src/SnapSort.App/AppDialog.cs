using System.Windows;
using System.Windows.Controls;

namespace SnapSort.App;

public static class AppDialog
{
    public static void Show(string message, string title)
    {
        var dialog = Create(title);

        var close = new Button
        {
            Content = "OK",
            Width = 92,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = Application.Current.FindResource("AccentButton") as Style
        };
        close.Click += (_, _) => dialog.Close();

        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        content.Children.Add(close);
        dialog.Content = content;
        dialog.ShowDialog();
    }

    public static bool Confirm(string message, string title)
    {
        var confirmed = false;
        var dialog = Create(title);
        var yes = new Button { Content = "Tak", Width = 92, Style = Application.Current.FindResource("AccentButton") as Style };
        var no = new Button { Content = "Nie", Width = 92, Margin = new Thickness(10, 0, 0, 0) };
        yes.Click += (_, _) => { confirmed = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        content.Children.Add(buttons);
        dialog.Content = content;
        dialog.ShowDialog();
        return confirmed;
    }

    private static Window Create(string title) => new()
    {
        Title = title,
        Width = 440,
        SizeToContent = SizeToContent.Height,
        ResizeMode = ResizeMode.NoResize,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = Application.Current.FindResource("AppBackgroundBrush") as System.Windows.Media.Brush,
        Style = Application.Current.FindResource("DialogWindowStyle") as Style,
        Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
    };
}
