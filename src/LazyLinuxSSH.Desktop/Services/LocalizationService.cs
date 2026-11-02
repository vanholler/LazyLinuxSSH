using System.Text.Json;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Infrastructure.Data;
using Microsoft.Win32;

namespace LazyLinuxSSH.Desktop.Services;

public sealed class LocalizationService : IAppLocalizationService
{
    private readonly string _preferencesPath;

    public LocalizationService(string? preferencesPath = null)
    {
        _preferencesPath = preferencesPath ?? Path.Combine(AppPaths.RootDirectory, "ui-preferences.json");
        CurrentLanguage = LoadLanguage();
        AppText.SetLanguage(CurrentLanguage);
    }

    public AppLanguage CurrentLanguage { get; private set; }
    public event EventHandler<AppLanguage>? LanguageChanged;

    public void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        AppText.SetLanguage(language);
        SaveLanguage(language);
        if (OperatingSystem.IsWindows())
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\LazyLinuxSSH", "Language", language == AppLanguage.English ? "en-US" : "ru-RU");
        LanguageChanged?.Invoke(this, language);
    }

    private AppLanguage LoadLanguage()
    {
        try
        {
            if (File.Exists(_preferencesPath))
            {
                var preferences = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_preferencesPath));
                if (preferences is not null) return ParseLanguage(preferences.Language);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged preferences file must not prevent the application from starting.
        }

        var environment = Environment.GetEnvironmentVariable("LAZYLINUXSSH_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(environment)) return ParseLanguage(environment);
        if (OperatingSystem.IsWindows())
        {
            var registryValue = Registry.GetValue(@"HKEY_CURRENT_USER\Software\LazyLinuxSSH", "Language", null) as string;
            if (!string.IsNullOrWhiteSpace(registryValue)) return ParseLanguage(registryValue);
        }
        return AppLanguage.Russian;
    }

    private void SaveLanguage(AppLanguage language)
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new UiPreferences
            {
                Language = language == AppLanguage.English ? "en-US" : "ru-RU"
            });
            File.WriteAllText(_preferencesPath, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Language switching remains available even if preferences are read-only.
        }
    }

    private static AppLanguage ParseLanguage(string? value) =>
        value?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            ? AppLanguage.English
            : AppLanguage.Russian;

    private sealed class UiPreferences
    {
        public string Language { get; set; } = "ru-RU";
    }
}
