using System;
using System.Windows;
using System.Windows.Input;

namespace SnapSort.App;

public static class WindowCommands
{
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
