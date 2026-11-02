using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Desktop.ViewModels;
using LazyLinuxSSH.Infrastructure.Commands;
using LazyLinuxSSH.Infrastructure.Data;
using LazyLinuxSSH.Infrastructure.Security;
using LazyLinuxSSH.Infrastructure.Ssh;
using LazyLinuxSSH.Infrastructure.Terminal;

namespace LazyLinuxSSH.Desktop.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLazyLinuxSsh(this IServiceCollection services)
    {
        services.AddDbContextFactory<LazyLinuxDbContext>(options =>
            options.UseSqlite($"Data Source={AppPaths.DatabasePath}"));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IConnectionRepository, ConnectionRepository>();
        services.AddSingleton<ICommandHistoryService, CommandHistoryService>();
        services.AddSingleton<ILocalShellSessionFactory, LocalShellSessionFactory>();
        services.AddSingleton<ICredentialProtector, CredentialProtector>();
        services.AddSingleton<IQuickCommandService, QuickCommandService>();
        services.AddSingleton<ICommandTemplateService, CommandTemplateService>();
        services.AddSingleton<ICommandSequenceExecutor, CommandSequenceExecutor>();
        services.AddSingleton<ISessionHealthMonitor, SessionHealthMonitor>();
        services.AddSingleton<ISshSessionManager, SshSessionManager>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppLocalizationService, LocalizationService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFileSaveDialogService, FileSaveDialogService>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }
}
