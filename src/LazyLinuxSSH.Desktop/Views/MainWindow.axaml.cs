using Avalonia.Controls;
using Avalonia.Input;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        WindowTitleBar.PointerPressed += OnTitleBarPointerPressed;
        Closing += OnWindowClosing;
    }

    public void ExitFromTray()
    {
        _exitRequested = true;
        Close();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_exitRequested) return;
        args.Cancel = true;
        Hide();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }
        BeginMoveDrag(e);
    }

    private void MinimizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ToggleWindowState();

    private void CloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void StartServerPicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConnectionProfile profile } &&
            DataContext is MainWindowViewModel viewModel)
        {
            StartConnectionButton.Flyout?.Hide();
            viewModel.ConnectCommand.Execute(profile);
            return;
        }

        StartConnectionButton.Flyout?.Hide();
    }

    private void ToggleWindowState() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K)
        {
            ServerSearchBox.Focus();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            CommandSearchBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && DataContext is MainWindowViewModel viewModel)
        {
            if (viewModel.IsConnectionEditorOpen)
            {
                viewModel.ConnectionEditor?.CancelCommand.Execute(null);
                e.Handled = true;
            }
            else if (viewModel.IsCustomCommandEditorOpen)
            {
                viewModel.CustomCommandEditor?.CancelCommand.Execute(null);
                e.Handled = true;
            }
            else if (viewModel.IsDeleteConfirmationOpen)
            {
                viewModel.CancelDeleteConnectionCommand.Execute(null);
                e.Handled = true;
            }
        }

        base.OnKeyDown(e);
    }
}
