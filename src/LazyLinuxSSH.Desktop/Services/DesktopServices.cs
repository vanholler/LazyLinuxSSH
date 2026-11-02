using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Services;

namespace LazyLinuxSSH.Desktop.Services;

public sealed class ThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public event EventHandler<AppTheme>? ThemeChanged;

    public void SetTheme(AppTheme theme)
    {
        if (Application.Current is null) return;
        CurrentTheme = theme;
        Application.Current.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeChanged?.Invoke(this, theme);
    }
}

public sealed class FileSaveDialogService : IFileSaveDialogService
{
    public async Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            throw new InvalidOperationException("Окно приложения ещё не готово.");
        }

        var storage = TopLevel.GetTopLevel(desktop.MainWindow)?.StorageProvider;
        if (storage is null) throw new InvalidOperationException("Системный диалог сохранения недоступен.");
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Скачать файл",
            SuggestedFileName = suggestedFileName,
            ShowOverwritePrompt = true
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path.LocalPath;
    }
}

public sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            throw new InvalidOperationException("Окно приложения ещё не готово.");
        }

        var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        if (clipboard is null) throw new InvalidOperationException("Буфер обмена недоступен.");
        await clipboard.SetTextAsync(text);
    }
}
