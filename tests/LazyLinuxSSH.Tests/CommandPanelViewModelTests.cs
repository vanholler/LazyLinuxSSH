using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Tests;

public sealed class CommandCatalogFilterTests
{
    [Fact]
    public void Filter_MatchesRussianNameCommandAndDescription()
    {
        var category = new QuickCommandCategoryViewModel(new QuickCommandCategory("Сеть", "network", [
            new QuickCommand("Проверить доступность", "ping -c 4 host", "ICMP-запросы"),
            new QuickCommand("DNS-записи", "dig example.com", "Проверка домена")
        ]));

        category.ApplyFilter("доступность");
        Assert.Single(category.Commands);
        Assert.True(category.IsExpanded);

        category.ApplyFilter("DIG");
        Assert.Equal("DNS-записи", Assert.Single(category.Commands).Name);

        category.ApplyFilter("домена");
        Assert.Single(category.Commands);

        category.ApplyFilter("не-существует");
        Assert.Empty(category.Commands);
        Assert.False(category.HasCommands);

        category.ApplyFilter("", true);
        Assert.Equal(2, category.Commands.Count);
        Assert.True(category.IsExpanded);
    }

    [Fact]
    public void Filter_CategoryName_KeepsWholeCategory()
    {
        var category = new QuickCommandCategoryViewModel(new QuickCommandCategory("Docker", "container", [
            new QuickCommand("Контейнеры", "docker ps -a", "Все контейнеры"),
            new QuickCommand("Образы", "docker images", "Локальные образы")
        ]));
        category.ApplyFilter("dock");
        Assert.Equal(2, category.Commands.Count);
    }
}

public sealed class ConnectionEditorVisibilityTests
{
    [Fact]
    public async Task EyeToggles_OnlyMask_SecretIsStillProtectedBeforeSaving()
    {
        var repository = new RecordingRepository();
        var group = new ConnectionGroup { Id = Guid.NewGuid(), Name = "Test" };
        repository.Groups.Add(group);
        var protector = new PrefixProtector();
        var editor = new ConnectionEditorViewModel(repository, protector, repository.Groups, null)
        {
            Name = "Server",
            Host = "127.0.0.1",
            Username = "root",
            Password = "plain-secret",
            Passphrase = "key-secret"
        };

        Assert.Equal('•', editor.PasswordMask);
        Assert.Equal('•', editor.PassphraseMask);
        editor.TogglePasswordVisibilityCommand.Execute(null);
        editor.TogglePassphraseVisibilityCommand.Execute(null);
        Assert.Equal('\0', editor.PasswordMask);
        Assert.Equal('\0', editor.PassphraseMask);
        Assert.Equal("plain-secret", editor.Password);
        Assert.Equal("key-secret", editor.Passphrase);

        await editor.SaveCommand.ExecuteAsync(null);
        var saved = Assert.IsType<ConnectionProfile>(repository.SavedProfile);
        Assert.Equal("protected:plain-secret", saved.EncryptedPassword);
        Assert.NotEqual("plain-secret", saved.EncryptedPassword);
        Assert.Null(saved.EncryptedPassphrase);

        editor.TogglePasswordVisibilityCommand.Execute(null);
        editor.TogglePassphraseVisibilityCommand.Execute(null);
        Assert.Equal('•', editor.PasswordMask);
        Assert.Equal('•', editor.PassphraseMask);
        editor.SetAuthenticationCommand.Execute("1");
        Assert.True(editor.IsPrivateKey);
        editor.SetAuthenticationCommand.Execute("0");
        Assert.True(editor.HasPassword);
    }

    private sealed class PrefixProtector : ICredentialProtector
    {
        public ValueTask<string?> ProtectAsync(string? plainText, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(plainText is null ? null : "protected:" + plainText);
        public ValueTask<string?> UnprotectAsync(string? protectedText, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedText);
    }

    private sealed class RecordingRepository : IConnectionRepository
    {
        public List<ConnectionGroup> Groups { get; } = [];
        public ConnectionProfile? SavedProfile { get; private set; }
        public Task<IReadOnlyList<ConnectionGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectionGroup>>(Groups);
        public Task<ConnectionProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionProfile?>(null);
        public Task<ConnectionGroup> AddGroupAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            SavedProfile = profile;
            return Task.CompletedTask;
        }
        public Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class DangerousCommandConfirmationTests
{
    [Fact]
    public async Task Execute_DangerousCommand_RequiresConfirmation_CopyDoesNot()
    {
        var clipboard = new RecordingClipboard();
        var viewModel = CreateViewModel(clipboard);
        var command = viewModel.BuiltInCategories[0].Commands[0];

        await viewModel.RunQuickCommandCommand.ExecuteAsync(command);
        Assert.True(viewModel.IsDangerousConfirmationOpen);
        Assert.True(command.IsSelected);
        Assert.Null(clipboard.Text);

        viewModel.CancelDangerousCommandCommand.Execute(null);
        Assert.False(viewModel.IsDangerousConfirmationOpen);

        viewModel.SetQuickModeCommand.Execute("True");
        await viewModel.RunQuickCommandCommand.ExecuteAsync(command);
        Assert.Equal("rm -rf /", clipboard.Text);
        Assert.False(viewModel.IsDangerousConfirmationOpen);
    }

    private static MainWindowViewModel CreateViewModel(IClipboardService clipboard) => new(
        new StubDatabaseInitializer(),
        new StubConnectionRepository(),
        new StubProtector(),
        new StubQuickCommandService(),
        new StubSequenceExecutor(),
        new StubSessionManager(),
        clipboard,
        new FakeFileSaveDialogService(),
        new StubThemeService(),
        new StubHistoryService(),
        new FakeLocalShellSessionFactory(),
        new FakeCommandTemplateService());

    private sealed class StubQuickCommandService : IQuickCommandService
    {
        public IReadOnlyList<QuickCommandCategory> GetBuiltInCategories() => [
            new QuickCommandCategory("Опасные", "danger", [new QuickCommand("Удалить всё", "rm -rf /", "Тест", true)])
        ];
        public Task<IReadOnlyList<CustomCommand>> GetCustomCommandsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CustomCommand>>([]);
        public Task<CustomCommand> SaveCustomCommandAsync(Guid? id, string name, IReadOnlyList<string> commands, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteCustomCommandAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class StubDatabaseInitializer : IDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubConnectionRepository : IConnectionRepository
    {
        public Task<IReadOnlyList<ConnectionGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectionGroup>>([]);
        public Task<ConnectionProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionProfile?>(null);
        public Task<ConnectionGroup> AddGroupAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubProtector : ICredentialProtector
    {
        public ValueTask<string?> ProtectAsync(string? plainText, CancellationToken cancellationToken = default) => ValueTask.FromResult(plainText);
        public ValueTask<string?> UnprotectAsync(string? protectedText, CancellationToken cancellationToken = default) => ValueTask.FromResult(protectedText);
    }

    private sealed class StubSequenceExecutor : ICommandSequenceExecutor
    {
        public Task ExecuteAsync(ISshSession session, IEnumerable<string> commands, TimeSpan delay, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSessionManager : ISshSessionManager
    {
        public ISshSession CreateSession(ConnectionProfile profile) => throw new NotSupportedException();
        public Task CloseSessionAsync(ISshSession session) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubHistoryService : ICommandHistoryService
    {
        public Task AddAsync(string commandText, string? serverName, CommandHistorySource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CommandHistoryEntry>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CommandHistoryEntry>>([]);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Dark;
        public event EventHandler<AppTheme>? ThemeChanged { add { } remove { } }
        public void SetTheme(AppTheme theme) { }
    }
}
