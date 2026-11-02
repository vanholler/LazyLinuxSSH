using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using LazyLinuxSSH.Desktop.Services;
using LazyLinuxSSH.Desktop.ViewModels;
using LazyLinuxSSH.Desktop.Views;

namespace LazyLinuxSSH.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    private int _cleanupStarted;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddLazyLinuxSsh();
            _services = services.BuildServiceProvider();
            var viewModel = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) =>
            {
                if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
                viewModel.CloseAllAsync().GetAwaiter().GetResult();
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(object? sender, EventArgs args)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null) return;
        var window = desktop.MainWindow;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ExitApplication(object? sender, EventArgs args)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (desktop.MainWindow is MainWindow window) window.ExitFromTray();
        else desktop.Shutdown();
    }
}
