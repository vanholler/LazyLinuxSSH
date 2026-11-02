using System.Text.Json;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Tests;

public sealed class ServerToolsTests
{
    [Fact]
    public async Task ToolsSection_SwitchesAndFiltersSavedScripts()
    {
        var service = new RecordingScriptService();
        service.Items.Add(CreateScript("Обновление пакетов", "apt update", "apt upgrade"));
        service.Items.Add(CreateScript("Диагностика системы", "df -h", "free -h"));
        var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsQuickCommandsActive);
        Assert.False(viewModel.IsScriptsActive);
        Assert.Equal(2, viewModel.VisibleCustomCommands.Count);

        viewModel.SetToolsSectionCommand.Execute("Scripts");
        Assert.True(viewModel.IsScriptsActive);
        Assert.False(viewModel.IsQuickCommandsActive);

        viewModel.ScriptSearchText = "free -h";
        var script = Assert.Single(viewModel.VisibleCustomCommands);
        Assert.Equal("Диагностика системы", script.Name);
        Assert.Equal("Bash", script.Kind);
        Assert.True(viewModel.HasVisibleCustomCommands);

        viewModel.ScriptSearchText = "отсутствует";
        Assert.Empty(viewModel.VisibleCustomCommands);
        Assert.True(viewModel.HasNoScriptResults);
        viewModel.ClearScriptSearchCommand.Execute(null);
        Assert.Equal(2, viewModel.VisibleCustomCommands.Count);
    }

    [Fact]
    public async Task ScriptEditor_EditPreservesIdAndUpdatesSequence()
    {
        var service = new RecordingScriptService();
        var original = CreateScript("Диагностика", "df -h", "free -h");
        service.Items.Add(original);
        var existing = CreateCard(original);
        var editor = new CustomCommandEditorViewModel(service, existing);

        Assert.True(editor.IsEditing);
        Assert.Equal("Редактирование скрипта", editor.Title);
        Assert.Equal("Диагностика", editor.Name);
        Assert.Equal(["df -h", "free -h"], editor.Lines.Select(line => line.Value));

        editor.Name = "Диагностика дисков";
        editor.Lines.RemoveAt(1);
        editor.Lines[0].Value = "df -hT";
        CustomCommand? saved = null;
        editor.Saved += (_, command) => saved = command;
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.Id, service.LastSavedId);
        Assert.Equal("Диагностика дисков", saved.Name);
        Assert.Equal(["df -hT"], QuickCommandServiceSequence(saved));
        Assert.Single(service.Items);
    }

    [Fact]
    public async Task ScriptCard_CopyDoesNotRun_AndRunIgnoresQuickCopyMode()
    {
        var service = new RecordingScriptService();
        service.Items.Add(CreateScript("Диагностика", "df -h", "free -h"));
        var clipboard = new FakeClipboardService();
        var sessionManager = new FakeSessionManager();
        var viewModel = CreateViewModel(service, clipboard, sessionManager);
        await viewModel.InitializeAsync();
        var script = Assert.Single(viewModel.VisibleCustomCommands);

        await script.CopyCommand.ExecuteAsync(null);
        Assert.Equal($"df -h{Environment.NewLine}free -h", clipboard.Text);
        Assert.Null(sessionManager.LastSession);

        var profile = new ConnectionProfile
        {
            Name = "API",
            Host = "127.0.0.1",
            Username = "root",
            EncryptedPassword = "encrypted"
        };
        await viewModel.ConnectCommand.ExecuteAsync(profile);
        viewModel.SetQuickModeCommand.Execute(true);
        await script.RunCommand.ExecuteAsync(null);

        Assert.Equal(["df -h\n", "free -h\n"], sessionManager.LastSession!.Sent);
        Assert.True(script.IsSelected);
        Assert.Equal($"df -h{Environment.NewLine}free -h", clipboard.Text);
    }

    [Fact]
    public async Task ScriptMenu_DeleteRemovesSavedEntityAndCard()
    {
        var service = new RecordingScriptService();
        service.Items.Add(CreateScript("Очистка журналов", "journalctl --vacuum-time=7d"));
        var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync();
        var script = Assert.Single(viewModel.VisibleCustomCommands);

        await script.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(service.Items);
        Assert.Empty(viewModel.CustomCommands);
        Assert.Empty(viewModel.VisibleCustomCommands);
        Assert.False(viewModel.HasCustomCommands);
        Assert.Equal("Скрипт удалён.", viewModel.StatusMessage);
    }

    private static CustomCommand CreateScript(string name, params string[] commands) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CommandsJson = JsonSerializer.Serialize(commands)
    };

    private static CustomCommandViewModel CreateCard(CustomCommand command) => new(
        command,
        QuickCommandServiceSequence(command),
        _ => Task.CompletedTask,
        _ => { },
        _ => Task.CompletedTask,
        _ => Task.CompletedTask);

    private static IReadOnlyList<string> QuickCommandServiceSequence(CustomCommand command) =>
        JsonSerializer.Deserialize<string[]>(command.CommandsJson) ?? [];

    private static MainWindowViewModel CreateViewModel(
        IQuickCommandService quickCommands,
        FakeClipboardService? clipboard = null,
        FakeSessionManager? sessionManager = null) => new(
        new FakeDatabaseInitializer(),
        new FakeConnectionRepository([]),
        new FakeProtector(),
        quickCommands,
        new FakeSequenceExecutor(),
        sessionManager ?? new FakeSessionManager(),
        clipboard ?? new FakeClipboardService(),
        new FakeFileSaveDialogService(),
        new FakeThemeService(),
        new FakeHistoryService(),
        new FakeLocalShellSessionFactory(),
        new FakeCommandTemplateService());

    private sealed class RecordingScriptService : IQuickCommandService
    {
        public List<CustomCommand> Items { get; } = [];
        public Guid? LastSavedId { get; private set; }

        public IReadOnlyList<QuickCommandCategory> GetBuiltInCategories() => [];

        public Task<IReadOnlyList<CustomCommand>> GetCustomCommandsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomCommand>>(Items.ToList());

        public Task<CustomCommand> SaveCustomCommandAsync(
            Guid? id,
            string name,
            IReadOnlyList<string> commands,
            CancellationToken cancellationToken = default)
        {
            LastSavedId = id;
            var item = Items.FirstOrDefault(command => command.Id == id) ?? new CustomCommand { Id = Guid.NewGuid() };
            item.Name = name.Trim();
            item.CommandsJson = JsonSerializer.Serialize(commands);
            if (!Items.Contains(item)) Items.Add(item);
            return Task.FromResult(item);
        }

        public Task DeleteCustomCommandAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(command => command.Id == id);
            return Task.CompletedTask;
        }
    }
}
