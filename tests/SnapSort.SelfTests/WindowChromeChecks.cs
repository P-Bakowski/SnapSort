using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SnapSort.App;
using SnapSort.App.Models;

internal static class WindowChromeChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources.Add("BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
                app.Resources.MergedDictionaries.Add(new ResourceDictionary());
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SnapSort;component/Styles.xaml", UriKind.Relative) });
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"/SnapSort;component/Themes/{theme}Theme.xaml", UriKind.Relative) };
                    foreach (var mode in new[] { "main", "photo", "video", "compare" })
                    {
                        Window window = mode switch
                        {
                            "main" => new MainWindow(),
                            "compare" => new CompareWindow(),
                            _ => new PreviewWindow()
                        };
                        try
                        {
                            window.WindowStartupLocation = WindowStartupLocation.Manual;
                            window.Left = SystemParameters.WorkArea.Left + 30;
                            window.Top = SystemParameters.WorkArea.Top + 30;
                            window.ShowActivated = false;
                            window.ShowInTaskbar = false;
                            window.Show();
                            // Test video controls without starting playback or opening a user file.
                            if (mode == "video") window.DataContext = new PhotoItem("chrome-test.mp4");
                            Drain(window);
                            var frame = window.Content as Border ?? (Border)VisualTreeHelper.GetChild(window, 0);
                            var normalBounds = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
                            for (var i = 0; i < 2; i++)
                            {
                                WindowCommands.ToggleMaximize.Execute(window);
                                Drain(window);
                                var content = (FrameworkElement)frame.Child;
                                var dpi = VisualTreeHelper.GetDpi(window);
                                var work = SystemParameters.WorkArea;
                                var topLeft = content.PointToScreen(new Point());
                                var bottomRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
                                if (topLeft.X < work.Left * dpi.DpiScaleX - 1 || topLeft.Y < work.Top * dpi.DpiScaleY - 1 ||
                                    bottomRight.X > work.Right * dpi.DpiScaleX + 1 || bottomRight.Y > work.Bottom * dpi.DpiScaleY + 1)
                                    throw new Exception($"{theme} {mode}: maximized content is clipped: {topLeft} to {bottomRight}, work area {work}.");
                                WindowCommands.ToggleMaximize.Execute(window);
                                Drain(window);
                                if (Math.Abs(window.Left - normalBounds.Left) > 1 || Math.Abs(window.Top - normalBounds.Top) > 1 ||
                                    Math.Abs(window.ActualWidth - normalBounds.Width) > 1 || Math.Abs(window.ActualHeight - normalBounds.Height) > 1)
                                    throw new Exception("Restore did not return the window to its original bounds.");
                            }
                        }
                        finally { window.Close(); }
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { app.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Window chrome checks failed.", failure);
        Console.WriteLine("Window chrome checks passed: main, photo, video and comparison, both themes, maximize and restore.");
    }

    private static void Drain(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
