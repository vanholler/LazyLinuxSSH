using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Tests;

public sealed class WorkspaceTabTests
{
    [Fact]
    public async Task StartTab_ShowsCreateOnlyWithoutProfiles_AndOpensLocalTerminal()
    {
        var localTerminal = new FakeLocalShellSessionFactory();
        var viewModel = CreateViewModel([], localTerminal: localTerminal);
        await viewModel.InitializeAsync();

        var tab = Assert.Single(viewModel.Tabs);
        Assert.Equal(WorkspaceTabKind.Start, tab.Kind);
        Assert.True(viewModel.IsStartTabActive);
        Assert.True(viewModel.ShowCreateConnection);
        Assert.False(viewModel.HasConnections);

        await viewModel.OpenLocalTerminalCommand.ExecuteAsync(null);
        Assert.Equal(1, localTerminal.CreateCount);
        Assert.Equal(1, localTerminal.Session.StartCount);
        var localTab = Assert.Single(viewModel.Tabs);
        Assert.Equal(WorkspaceTabKind.Terminal, localTab.Kind);
        Assert.Same(localTab, viewModel.ActiveTab);
        var localSession = Assert.IsType<LocalTerminalViewModel>(viewModel.ActiveSession);
        Assert.True(localSession.IsConnected);

        await localSession.SendTextAsync("echo embedded\r");
        Assert.Equal("echo embedded\r", Assert.Single(localTerminal.Session.Sent));

        var profile = CreateProfile("Production API");
        var group = new ConnectionGroup { Name = "Main", Connections = [profile] };
        var populated = CreateViewModel([group]);
        await populated.InitializeAsync();
        Assert.True(populated.HasConnections);
        Assert.False(populated.ShowCreateConnection);
    }

    [Fact]
    public async Task StartConnectionPicker_ListsSavedProfiles_AndOnlyNonEmptyGroupsExpand()
    {
        var profile = CreateProfile("Production API");
        var populatedGroup = new ConnectionGroup { Name = "Main", Connections = [profile] };
        var emptyGroup = new ConnectionGroup { Name = "Empty" };
        var viewModel = CreateViewModel([populatedGroup, emptyGroup]);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasConnections);
        Assert.False(viewModel.ShowCreateConnection);
        Assert.Equal(profile, Assert.Single(viewModel.SavedConnections));

        var populated = viewModel.VisibleGroups.Single(group => group.Name == "Main");
        var empty = viewModel.VisibleGroups.Single(group => group.Name == "Empty");
        Assert.True(populated.HasConnections);
        Assert.True(populated.IsExpanded);
        Assert.False(empty.HasConnections);
        Assert.False(empty.IsExpanded);

        empty.ToggleExpandedCommand.Execute(null);
        Assert.False(empty.IsExpanded);
        populated.ToggleExpandedCommand.Execute(null);
        Assert.False(populated.IsExpanded);
        Assert.Equal(-90, populated.ChevronAngle);

        viewModel.ServerSearchText = "Production";
        Assert.False(Assert.Single(viewModel.VisibleGroups).IsExpanded);
        viewModel.ServerSearchText = string.Empty;
        populated = viewModel.VisibleGroups.Single(group => group.Name == "Main");
        populated.ToggleExpandedCommand.Execute(null);
        Assert.True(populated.IsExpanded);
        Assert.Equal(0, populated.ChevronAngle);
    }

    [Fact]
    public async Task ProfileClick_AlwaysCreatesNewTab_WithShortServerName()
    {
        var profile = CreateProfile("Very Long Production Server Name");
        var viewModel = CreateViewModel([new ConnectionGroup { Name = "Main", Connections = [profile] }]);
        await viewModel.InitializeAsync();

        await viewModel.ConnectCommand.ExecuteAsync(profile);
        await viewModel.ConnectCommand.ExecuteAsync(profile);

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal(3, viewModel.Tabs.Count);
        Assert.Equal(WorkspaceTabKind.Terminal, viewModel.ActiveTab!.Kind);
        Assert.True(viewModel.ActiveTab.Title.Length <= 22);
        Assert.EndsWith("…", viewModel.ActiveTab.Title);
        Assert.NotSame(viewModel.Tabs[1], viewModel.Tabs[2]);
    }

    [Fact]
    public async Task History_OpensOnce_CopiesEntryAndClears()
    {
        var history = new FakeHistoryService();
        var clipboard = new FakeClipboardService();
        history.Items.Add(new CommandHistoryEntry { CommandText = "systemctl status nginx", ServerName = "API" });
        var viewModel = CreateViewModel([], history, clipboard);

        await viewModel.OpenHistoryCommand.ExecuteAsync(null);
        await viewModel.OpenHistoryCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.Equal(WorkspaceTabKind.History, viewModel.ActiveTab!.Kind);
        Assert.Equal("История", viewModel.ActiveTab.Title);
        var entry = Assert.Single(viewModel.HistoryEntries);

        await viewModel.CopyHistoryEntryCommand.ExecuteAsync(entry);
        Assert.Equal("systemctl status nginx", clipboard.Text);

        await viewModel.ClearHistoryCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.HistoryEntries);
        Assert.True(history.WasCleared);
        Assert.False(viewModel.HasHistory);
    }

    [Fact]
    public async Task LayoutSwitch_ReusesPersistentSlots_AndQuickCommandStaysInWorkspace()
    {
        var profile = CreateProfile("API");
        var viewModel = CreateViewModel([new ConnectionGroup { Name = "Main", Connections = [profile] }]);
        await viewModel.InitializeAsync();
        await viewModel.ConnectCommand.ExecuteAsync(profile);
        await viewModel.ConnectCommand.ExecuteAsync(profile);

        var slots = viewModel.TerminalSlots.ToArray();
        viewModel.SetLayoutCommand.Execute("1");
        var firstGridTerminal = viewModel.TerminalSlots[0].Terminal;
        Assert.Equal(4, viewModel.TerminalSlots.Count(slot => slot.IsVisible));
        Assert.Equal(3, viewModel.TerminalSlots.Count(slot => slot.IsEmpty));
        viewModel.SetLayoutCommand.Execute("2");
        Assert.Equal(16, viewModel.TerminalSlots.Count(slot => slot.IsVisible));
        Assert.Equal(15, viewModel.TerminalSlots.Count(slot => slot.IsEmpty));
        Assert.Same(firstGridTerminal, viewModel.TerminalSlots[0].Terminal);
        viewModel.SetLayoutCommand.Execute("0");
        Assert.Single(viewModel.TerminalSlots, slot => slot.IsVisible);
        Assert.Equal(slots, viewModel.TerminalSlots);

        var command = new QuickCommandViewModel(new QuickCommand("Текущий каталог", "pwd", "Тест"));
        await viewModel.RunQuickCommandCommand.ExecuteAsync(command);
        Assert.Equal(3, viewModel.Tabs.Count);
        Assert.True(viewModel.IsTerminalTabActive);
        Assert.Equal("Команда «Текущий каталог» отправлена в активный терминал.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Layouts_AreIndependentPerTab_AndEmptySlotsAcceptOtherOpenSessions()
    {
        var profile = CreateProfile("API");
        var viewModel = CreateViewModel([new ConnectionGroup { Name = "Main", Connections = [profile] }]);
        await viewModel.InitializeAsync();
        await viewModel.ConnectCommand.ExecuteAsync(profile);
        var firstTab = viewModel.ActiveTab!;
        var firstSession = viewModel.ActiveSession!;
        await viewModel.ConnectCommand.ExecuteAsync(profile);
        var secondTab = viewModel.ActiveTab!;
        var secondSession = viewModel.ActiveSession!;

        viewModel.SetLayoutCommand.Execute("1");
        Assert.Equal(4, viewModel.TerminalSlots.Count(slot => slot.IsVisible));
        Assert.Same(secondSession, viewModel.TerminalSlots[0].Terminal);
        var choice = Assert.Single(viewModel.TerminalSlots[1].AvailableSessions);
        Assert.Same(firstSession, choice.Session);
        choice.SelectCommand.Execute(null);
        Assert.Same(firstSession, viewModel.TerminalSlots[1].Terminal);
        Assert.Empty(viewModel.TerminalSlots[2].AvailableSessions);

        viewModel.ActivateTabCommand.Execute(firstTab);
        Assert.Equal(0, viewModel.LayoutMode);
        Assert.Single(viewModel.TerminalSlots, slot => slot.IsVisible);
        viewModel.SetLayoutCommand.Execute("2");
        Assert.Equal(16, viewModel.TerminalSlots.Count(slot => slot.IsVisible));
        Assert.Equal(2, firstTab.LayoutMode);
        Assert.Equal(1, secondTab.LayoutMode);

        viewModel.ActivateTabCommand.Execute(secondTab);
        Assert.Equal(1, viewModel.LayoutMode);
        Assert.Equal(4, viewModel.TerminalSlots.Count(slot => slot.IsVisible));
        Assert.Same(secondSession, viewModel.TerminalSlots[0].Terminal);
        Assert.Same(firstSession, viewModel.TerminalSlots[1].Terminal);
        Assert.Equal(2, firstTab.LayoutMode);
    }

    [Fact]
    public async Task ClosingAssignedSession_RemovesItFromOtherTabGrid_ButKeepsHostTab()
    {
        var profile = CreateProfile("API");
        var viewModel = CreateViewModel([new ConnectionGroup { Name = "Main", Connections = [profile] }]);
        await viewModel.InitializeAsync();
        await viewModel.ConnectCommand.ExecuteAsync(profile);
        var firstTab = viewModel.ActiveTab!;
        var firstSession = viewModel.ActiveSession!;
        await viewModel.ConnectCommand.ExecuteAsync(profile);
        var secondTab = viewModel.ActiveTab!;
        var secondSession = viewModel.ActiveSession!;

        viewModel.SetLayoutCommand.Execute("1");
        viewModel.TerminalSlots[1].AvailableSessions.Single().SelectCommand.Execute(null);
        Assert.Same(firstSession, viewModel.ActiveSession);

        await firstTab.CloseCommand.ExecuteAsync(null);

        Assert.Same(secondTab, viewModel.ActiveTab);
        Assert.Same(secondSession, viewModel.ActiveSession);
        Assert.Contains(secondTab, viewModel.Tabs);
        Assert.DoesNotContain(firstTab, viewModel.Tabs);
        Assert.Null(secondTab.GetGridSession(1));
        Assert.Null(viewModel.TerminalSlots[1].Terminal);
        Assert.True(viewModel.TerminalSlots[1].IsEmpty);
    }

    [Fact]
    public async Task LocalTerminal_WindowsStyleEcho_DisplaysInputAndBackspace()
    {
        var factory = new FakeLocalShellSessionFactory();
        factory.Session.RequiresLocalEcho = true;
        var viewModel = CreateViewModel([], localTerminal: factory);
        await viewModel.InitializeAsync();
        await viewModel.OpenLocalTerminalCommand.ExecuteAsync(null);
        var terminal = viewModel.ActiveSession!;

        await terminal.SendTextAsync("echX");
        await terminal.SendTextAsync("\u007f");
        await terminal.SendTextAsync("o embedded\r");

        Assert.Equal(["echX", "\u007f", "o embedded\r"], factory.Session.Sent);
        Assert.EndsWith("echo embedded\n", terminal.Buffer.GetSnapshot().Text);
    }

    [Fact]
    public void ConnectionsPanel_CollapsesToIconRail_AndRestores()
    {
        var viewModel = CreateViewModel([]);
        Assert.True(viewModel.IsLeftPanelVisible);
        Assert.Equal(300, viewModel.LeftPanelWidth);

        viewModel.ToggleLeftPanelCommand.Execute(null);
        Assert.False(viewModel.IsLeftPanelVisible);
        Assert.True(viewModel.IsLeftPanelCollapsed);
        Assert.Equal(64, viewModel.LeftPanelWidth);

        viewModel.ToggleLeftPanelCommand.Execute(null);
        Assert.True(viewModel.IsLeftPanelVisible);
        Assert.Equal(300, viewModel.LeftPanelWidth);
    }

    [Fact]
    public async Task SettingsTemplates_SaveFilterAndRestoreDefault_AndAccordionKeepsOneCategoryOpen()
    {
        var viewModel = CreateViewModel([], quickCommands: new TestCatalogQuickCommandService());
        await viewModel.InitializeAsync();
        Assert.Equal(2, viewModel.BuiltInCategories.Count);

        viewModel.BuiltInCategories[1].IsExpanded = true;
        Assert.False(viewModel.BuiltInCategories[0].IsExpanded);
        Assert.True(viewModel.BuiltInCategories[1].IsExpanded);

        viewModel.OpenSettingsCommand.Execute(null);
        Assert.True(viewModel.IsSettingsTabActive);
        Assert.True(viewModel.IsDefaultTemplate);
        Assert.False(viewModel.CanEditTemplate);
        Assert.Equal(2, viewModel.TemplateTree.Count);
        Assert.DoesNotContain(viewModel.TemplateTree, category => category.Name == "Custom");

        viewModel.NewCommandTemplateCommand.Execute(null);
        viewModel.TemplateName = "Только сеть";
        viewModel.TemplateTree[0].Commands[0].IsSelected = true;
        viewModel.TemplateTree[0].Commands[1].IsSelected = false;
        viewModel.TemplateTree[1].IsChecked = false;
        await viewModel.SaveCommandTemplateCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.CommandTemplates.Count);
        Assert.Equal("Только сеть", viewModel.ActiveTemplateName);
        var category = Assert.Single(viewModel.BuiltInCategories);
        Assert.Equal("Сеть", category.Name);
        Assert.Equal("ping -c 4 host", Assert.Single(category.Commands).Command);

        var defaultTemplate = viewModel.CommandTemplates[0];
        viewModel.SelectCommandTemplateCommand.Execute(defaultTemplate);
        viewModel.ApplySelectedTemplateCommand.Execute(null);
        Assert.Equal(2, viewModel.BuiltInCategories.Count);
        Assert.Equal("Default", viewModel.ActiveTemplateName);
    }

    private static ConnectionProfile CreateProfile(string name) => new()
    {
        Name = name,
        Host = "192.168.1.10",
        Username = "root",
        EncryptedPassword = "encrypted"
    };

    private static MainWindowViewModel CreateViewModel(
        IReadOnlyList<ConnectionGroup> groups,
        FakeHistoryService? history = null,
        FakeClipboardService? clipboard = null,
        FakeLocalShellSessionFactory? localTerminal = null,
        IQuickCommandService? quickCommands = null) => new(
        new FakeDatabaseInitializer(),
        new FakeConnectionRepository(groups),
        new FakeProtector(),
        quickCommands ?? new FakeQuickCommandService(),
        new FakeSequenceExecutor(),
        new FakeSessionManager(),
        clipboard ?? new FakeClipboardService(),
        new FakeFileSaveDialogService(),
        new FakeThemeService(),
        history ?? new FakeHistoryService(),
        localTerminal ?? new FakeLocalShellSessionFactory(),
        new FakeCommandTemplateService());
}

public sealed class TerminalHistoryTests
{
    [Fact]
    public async Task InteractiveInput_RecordsCompletedCommands_AndHandlesBackspaceAndMultilinePaste()
    {
        var history = new FakeHistoryService();
        var terminal = CreateTerminal(history);
        await terminal.ConnectAsync();

        await terminal.SendTextAsync("ls -la");
        await terminal.SendTextAsync("\u007f");
        await terminal.SendTextAsync("a\r");
        await terminal.SendTextAsync("pwd\rwhoami\r");

        Assert.Equal(["ls -la", "pwd", "whoami"], history.Items.Select(item => item.CommandText));
        Assert.All(history.Items, item => Assert.Equal(CommandHistorySource.Interactive, item.Source));
    }

    [Fact]
    public async Task SecretPromptInput_IsNotRecorded()
    {
        var history = new FakeHistoryService();
        var terminal = CreateTerminal(history);
        await terminal.ConnectAsync();

        terminal.Buffer.Append("root@server:~# sudo -i\r\n[sudo] password for root: ");
        await terminal.SendTextAsync("SuperSecret\r");
        Assert.Empty(history.Items);

        terminal.Buffer.Append("\r\nroot@server:~# ");
        await terminal.SendTextAsync("echo ok\r");
        Assert.Equal("echo ok", Assert.Single(history.Items).CommandText);
    }

    [Fact]
    public async Task QuickSequence_RecordsEachSentCommandOnce()
    {
        var history = new FakeHistoryService();
        var terminal = CreateTerminal(history);
        await terminal.ConnectAsync();

        await terminal.ExecuteCommandsAsync(["pwd", "ls -la"]);

        Assert.Equal(["pwd", "ls -la"], history.Items.Select(item => item.CommandText));
        Assert.All(history.Items, item => Assert.Equal(CommandHistorySource.QuickCommand, item.Source));
    }

    private static TerminalSessionViewModel CreateTerminal(FakeHistoryService history) => new(
        new ConnectionProfile { Name = "API", Host = "localhost", Username = "root" },
        new FakeSessionManager(),
        new FakeSequenceExecutor(),
        history,
        new FakeClipboardService(),
        new FakeFileSaveDialogService(),
        _ => Task.CompletedTask,
        _ => { });
}

internal sealed class FakeHistoryService : ICommandHistoryService
{
    public List<CommandHistoryEntry> Items { get; } = [];
    public bool WasCleared { get; private set; }

    public Task AddAsync(string commandText, string? serverName, CommandHistorySource source, CancellationToken cancellationToken = default)
    {
        Items.Add(new CommandHistoryEntry { CommandText = commandText, ServerName = serverName, Source = source });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CommandHistoryEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommandHistoryEntry>>(Items.OrderByDescending(item => item.ExecutedAt).ToList());

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        WasCleared = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeClipboardService : IClipboardService
{
    public string? Text { get; private set; }
    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

internal sealed class FakeFileSaveDialogService : IFileSaveDialogService
{
    public string? PathToReturn { get; set; }
    public string? SuggestedFileName { get; private set; }

    public Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        SuggestedFileName = suggestedFileName;
        return Task.FromResult(PathToReturn);
    }
}

internal sealed class FakeCommandTemplateService : ICommandTemplateService
{
    private readonly List<CommandTemplate> _items = [];

    public Task<IReadOnlyList<CommandTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CommandTemplate> result = [new CommandTemplate
        {
            Id = CommandTemplate.DefaultId,
            Name = "Default",
            IsDefault = true
        }, .. _items];
        return Task.FromResult(result);
    }

    public Task<CommandTemplate> SaveCustomAsync(Guid? id, string name, IReadOnlyCollection<string> selectedCommandKeys, CancellationToken cancellationToken = default)
    {
        var item = _items.FirstOrDefault(template => template.Id == id) ?? new CommandTemplate { Id = Guid.NewGuid() };
        item.Name = name;
        item.SelectedCommandKeysJson = System.Text.Json.JsonSerializer.Serialize(selectedCommandKeys);
        if (!_items.Contains(item)) _items.Add(item);
        return Task.FromResult(item);
    }
}

internal abstract class FakeFileTransferSession : IFileTransferSession
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    public List<(string Path, string Content)> Writes { get; } = [];
    public List<(string RemotePath, string LocalPath)> Downloads { get; } = [];
    public string? WorkingDirectory { get; set; }
    public string FakeHomeDirectory { get; set; } = "/root";

    public Task<TextFileContent> ReadTextFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(path);
        return Task.FromResult(Files.TryGetValue(fullPath, out var content)
            ? new TextFileContent(fullPath, content, true)
            : new TextFileContent(fullPath, string.Empty, false));
    }

    public Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(path);
        Files[fullPath] = content;
        Writes.Add((fullPath, content));
        return Task.CompletedTask;
    }

    public Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        Downloads.Add((Resolve(remotePath), localPath));
        return Task.CompletedTask;
    }

    public List<(string LocalPath, string RemoteFileName)> Uploads { get; } = [];

    public Task UploadFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken = default)
    {
        Uploads.Add((localPath, remoteFileName));
        var fullPath = Resolve(Path.GetFileName(remoteFileName.Replace('\\', '/')));
        Files[fullPath] = File.Exists(localPath) ? File.ReadAllText(localPath) : string.Empty;
        return Task.CompletedTask;
    }

    private string Resolve(string path) =>
        ShellWorkingDirectory.ResolveAgainstHome(WorkingDirectory, FakeHomeDirectory, path);
}

internal sealed class FakeLocalShellSessionFactory : ILocalShellSessionFactory
{
    public FakeLocalShellSession Session { get; } = new();
    public int CreateCount { get; private set; }

    public ILocalShellSession CreateSession()
    {
        CreateCount++;
        return Session;
    }
}

internal sealed class FakeLocalShellSession : FakeFileTransferSession, ILocalShellSession
{
    public string ShellName => "fake-shell";
    public bool IsRunning { get; private set; }
    public bool RequiresLocalEcho { get; set; }
    public int StartCount { get; private set; }
    public List<string> Sent { get; } = [];
    public event EventHandler<string>? OutputReceived { add { } remove { } }
    public event EventHandler? Exited { add { } remove { } }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        Sent.Add(text);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsRunning = false;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeSessionManager : ISshSessionManager
{
    public FakeSshSession? LastSession { get; private set; }
    public ISshSession CreateSession(ConnectionProfile profile) => LastSession = new FakeSshSession(profile);
    public Task CloseSessionAsync(ISshSession session) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeSshSession(ConnectionProfile profile) : FakeFileTransferSession, ISshSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public ConnectionProfile Profile { get; } = profile;
    public SshSessionState State { get; private set; } = SshSessionState.Disconnected;
    public DateTimeOffset? LastOutputAt => DateTimeOffset.UtcNow;
    public List<string> Sent { get; } = [];
    public event EventHandler<string>? OutputReceived { add { } remove { } }
    public event EventHandler<SshSessionState>? StateChanged { add { } remove { } }
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = SshSessionState.Connected;
        return Task.CompletedTask;
    }
    public Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        Sent.Add(text);
        return Task.CompletedTask;
    }
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public void MarkStale() => State = SshSessionState.Stale;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeSequenceExecutor : ICommandSequenceExecutor
{
    public async Task ExecuteAsync(ISshSession session, IEnumerable<string> commands, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        foreach (var command in commands) await session.SendAsync(command + "\n", cancellationToken);
    }
}

internal sealed class FakeDatabaseInitializer : IDatabaseInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeConnectionRepository(IReadOnlyList<ConnectionGroup> groups) : IConnectionRepository
{
    public Task<IReadOnlyList<ConnectionGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult(groups);
    public Task<ConnectionProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionProfile?>(null);
    public Task<ConnectionGroup> AddGroupAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeProtector : ICredentialProtector
{
    public ValueTask<string?> ProtectAsync(string? plainText, CancellationToken cancellationToken = default) => ValueTask.FromResult(plainText);
    public ValueTask<string?> UnprotectAsync(string? protectedText, CancellationToken cancellationToken = default) => ValueTask.FromResult(protectedText);
}

internal sealed class TestCatalogQuickCommandService : IQuickCommandService
{
    public IReadOnlyList<QuickCommandCategory> GetBuiltInCategories() =>
    [
        new QuickCommandCategory("Сеть", "◎", [
            new QuickCommand("Проверить доступность", "ping -c 4 host", "Тест"),
            new QuickCommand("Маршруты", "ip route", "Тест")
        ]),
        new QuickCommandCategory("Файлы", "▤", [new QuickCommand("Список", "ls -la", "Тест")])
    ];
    public Task<IReadOnlyList<CustomCommand>> GetCustomCommandsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CustomCommand>>([]);
    public Task<CustomCommand> SaveCustomCommandAsync(Guid? id, string name, IReadOnlyList<string> commands, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteCustomCommandAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeQuickCommandService : IQuickCommandService
{
    public IReadOnlyList<QuickCommandCategory> GetBuiltInCategories() => [];
    public Task<IReadOnlyList<CustomCommand>> GetCustomCommandsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CustomCommand>>([]);
    public Task<CustomCommand> SaveCustomCommandAsync(Guid? id, string name, IReadOnlyList<string> commands, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteCustomCommandAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeThemeService : IThemeService
{
    public AppTheme CurrentTheme => AppTheme.Dark;
    public event EventHandler<AppTheme>? ThemeChanged { add { } remove { } }
    public void SetTheme(AppTheme theme) { }
}
