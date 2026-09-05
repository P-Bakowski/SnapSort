using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnapSort.App;

public static class WindowCommands
{
    public static readonly DependencyProperty FitToWorkAreaProperty = DependencyProperty.RegisterAttached(
        "FitToWorkArea", typeof(bool), typeof(WindowCommands), new PropertyMetadata(false, FitToWorkAreaChanged));

    public static bool GetFitToWorkArea(Window window) => (bool)window.GetValue(FitToWorkAreaProperty);
    public static void SetFitToWorkArea(Window window, bool value) => window.SetValue(FitToWorkAreaProperty, value);

    private static void FitToWorkAreaChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Window window) return;
        window.SourceInitialized -= InitializeWorkArea;
        if (PresentationSource.FromVisual(window) is HwndSource source)
        {
            source.RemoveHook(WorkAreaHook);
            if ((bool)e.NewValue) source.AddHook(WorkAreaHook);
        }
        else if ((bool)e.NewValue) window.SourceInitialized += InitializeWorkArea;
    }

    private static void InitializeWorkArea(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        window.SourceInitialized -= InitializeWorkArea;
        ((HwndSource)PresentationSource.FromVisual(window)).AddHook(WorkAreaHook);
    }

    private static IntPtr WorkAreaHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        const int monitorDefaultToNearest = 2;
        if (message != wmGetMinMaxInfo) return IntPtr.Zero;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, monitorDefaultToNearest), ref monitor)) return IntPtr.Zero;

        // Custom chrome has no native frame to hide outside the monitor's work area.
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition.X = monitor.Work.Left - monitor.Monitor.Left;
        bounds.MaxPosition.Y = monitor.Work.Top - monitor.Monitor.Top;
        bounds.MaxSize.X = monitor.Work.Right - monitor.Work.Left;
        bounds.MaxSize.Y = monitor.Work.Bottom - monitor.Work.Top;
        Marshal.StructureToPtr(bounds, lParam, false);
        // Let WPF continue enforcing the window's minimum resize dimensions.
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public static ICommand Close { get; } = new CloseWindowCommand();
    public static ICommand Minimize { get; } = new WindowActionCommand(window => window.WindowState = WindowState.Minimized);
    public static ICommand ToggleMaximize { get; } = new WindowActionCommand(window =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);

    private sealed class WindowActionCommand(Action<Window> action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => parameter is Window;
        public void Execute(object? parameter)
        {
            if (parameter is Window window)
                action(window);
        }
    }

    private sealed class CloseWindowCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public bool CanExecute(object? parameter) => parameter is Window;
        public void Execute(object? parameter)
        {
            if (parameter is Window window)
            {
                window.Close();
            }
        }
    }
}
