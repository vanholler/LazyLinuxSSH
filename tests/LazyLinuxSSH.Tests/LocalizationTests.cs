using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Desktop.Services;
using LazyLinuxSSH.Desktop.ViewModels;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace LazyLinuxSSH.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void SettingsInterface_SwitchesSectionsAndTranslatesWorkspaceInBothDirections()
    {
        var path = TestPreferencesPath();
        var localization = new LocalizationService(path);
        try
        {
            var viewModel = CreateViewModel(localization);
            viewModel.OpenSettingsCommand.Execute(null);
            Assert.True(viewModel.IsTemplatesSettingsActive);
            Assert.False(viewModel.IsInterfaceSettingsActive);

            viewModel.SetSettingsSectionCommand.Execute("Interface");
            Assert.True(viewModel.IsInterfaceSettingsActive);
            Assert.False(viewModel.IsTemplatesSettingsActive);

            viewModel.SetLanguageCommand.Execute("English");
            Assert.True(viewModel.IsEnglishLanguage);
            Assert.False(viewModel.IsRussianLanguage);
            Assert.Equal("Server tools", AppText.T("Инструменты сервера"));
            Assert.Equal("New script", AppText.T("Новый скрипт"));

            viewModel.SetLanguageCommand.Execute("Russian");
            Assert.True(viewModel.IsRussianLanguage);
            Assert.Equal("Инструменты сервера", AppText.T("Инструменты сервера"));
        }
        finally
        {
            AppText.SetLanguage(AppLanguage.Russian);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task TemplateTree_StartsCollapsedAndChecked_AndOnlyOneCategoryExpands()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.OpenSettingsCommand.Execute(null);

        Assert.All(viewModel.TemplateTree, category => Assert.False(category.IsExpanded));
        Assert.All(viewModel.TemplateTree, category => Assert.True(category.IsChecked));
        Assert.Equal(-90, viewModel.TemplateTree[0].ChevronAngle);

        viewModel.TemplateTree[0].ToggleExpandedCommand.Execute(null);
        Assert.True(viewModel.TemplateTree[0].IsExpanded);
        Assert.Equal(0, viewModel.TemplateTree[0].ChevronAngle);
        viewModel.TemplateTree[1].ToggleExpandedCommand.Execute(null);
        Assert.False(viewModel.TemplateTree[0].IsExpanded);
        Assert.True(viewModel.TemplateTree[1].IsExpanded);
    }

    [Fact]
    public void LanguagePreference_PersistsAcrossServiceInstances()
    {
        var path = TestPreferencesPath();
        try
        {
            var first = new LocalizationService(path);
            first.SetLanguage(AppLanguage.English);
            var second = new LocalizationService(path);
            Assert.Equal(AppLanguage.English, second.CurrentLanguage);
            second.SetLanguage(AppLanguage.Russian);
            Assert.Equal(AppLanguage.Russian, new LocalizationService(path).CurrentLanguage);
        }
        finally
        {
            AppText.SetLanguage(AppLanguage.Russian);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string TestPreferencesPath() =>
        Path.Combine(Path.GetTempPath(), $"lazy-localization-{Guid.NewGuid():N}.json");

    private static MainWindowViewModel CreateViewModel(IAppLocalizationService? localization = null) => new(
        new FakeDatabaseInitializer(),
        new FakeConnectionRepository([]),
        new FakeProtector(),
        new TestCatalogQuickCommandService(),
        new FakeSequenceExecutor(),
        new FakeSessionManager(),
        new FakeClipboardService(),
        new FakeFileSaveDialogService(),
        new FakeThemeService(),
        new FakeHistoryService(),
        new FakeLocalShellSessionFactory(),
        new FakeCommandTemplateService(),
        localization);
}
