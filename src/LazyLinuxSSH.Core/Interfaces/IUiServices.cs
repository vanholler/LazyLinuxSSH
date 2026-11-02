using LazyLinuxSSH.Core.Services;

namespace LazyLinuxSSH.Core.Interfaces;

public enum AppLanguage
{
    Russian,
    English
}

public interface IAppLocalizationService
{
    AppLanguage CurrentLanguage { get; }
    event EventHandler<AppLanguage>? LanguageChanged;
    void SetLanguage(AppLanguage language);
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    event EventHandler<AppTheme>? ThemeChanged;
    void SetTheme(AppTheme theme);
}

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

public interface IFileSaveDialogService
{
    Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}
