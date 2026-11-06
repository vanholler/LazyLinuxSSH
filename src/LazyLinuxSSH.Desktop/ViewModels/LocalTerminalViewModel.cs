using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class LocalTerminalViewModel : TerminalViewModelBase
{
    private readonly ILocalShellSession _session;

    public LocalTerminalViewModel(
        ILocalShellSessionFactory sessionFactory,
        ICommandHistoryService historyService,
        IClipboardService clipboardService,
        IFileSaveDialogService fileSaveDialogService,
        Func<TerminalViewModelBase, Task> closeRequested,
        Action<TerminalViewModelBase> activated)
        : base(historyService, clipboardService, fileSaveDialogService, closeRequested, activated)
    {
        _session = sessionFactory.CreateSession();
        Title = OperatingSystem.IsWindows() ? "CMD" : "Локальный shell";
        StatusMessage = "Локальный терминал";
    }

    public override bool IsConnected => _session.IsRunning && State == LocalTerminalState.Running;
    public override string StateLabel => IsConnected ? _session.ShellName : "Остановлен";
    protected override string HistoryServerName => "Локальный компьютер";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private LocalTerminalState _state = LocalTerminalState.Stopped;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_session.IsRunning) return;
        IsBusy = true;
        State = LocalTerminalState.Starting;
        StatusMessage = "Запуск локального shell…";
        try
        {
            _session.OutputReceived += OnOutputReceived;
            _session.Exited += OnExited;
            await _session.StartAsync(cancellationToken);
            State = _session.IsRunning ? LocalTerminalState.Running : LocalTerminalState.Stopped;
            StatusMessage = IsConnected ? $"Локальный shell: {_session.ShellName}" : "Локальный shell завершил работу.";
            Buffer.Append($"[LazyLinuxSSH · {_session.ShellName} · локальный терминал]\r\n");
        }
        catch (Exception exception)
        {
            State = LocalTerminalState.Stopped;
            StatusMessage = $"Не удалось запустить локальный shell: {exception.Message}";
            Buffer.Append($"\r\n[Ошибка: {exception.Message}]\r\n");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            StatusMessage = "Локальный shell не запущен.";
            return;
        }

        try
        {
            var editorCommand = GetCompletedEditorCommand(text);
            if (editorCommand is not null && Core.Services.EditorCommandParser.TryParse(editorCommand, out var filePath))
            {
                ResetInteractiveLine();
                EchoLocalInputIfRequired("\r");
                await _session.SendAsync("\u0015\r", cancellationToken);
                await Task.Delay(40, cancellationToken);
                EchoInterceptedCommand(editorCommand);
                await TryRecordAsync(editorCommand, CommandHistorySource.Interactive, cancellationToken);
                await OpenFileEditorAsync(_session, filePath, cancellationToken);
                return;
            }

            EchoLocalInputIfRequired(text);
            await _session.SendAsync(text, cancellationToken);
            await CaptureInteractiveInputAsync(text, cancellationToken);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public override async Task ExecuteCommandsAsync(
        IEnumerable<string> commands,
        CommandHistorySource source = CommandHistorySource.QuickCommand,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            StatusMessage = "Локальный shell не запущен.";
            return;
        }

        try
        {
            var sequence = commands.Where(command => !string.IsNullOrWhiteSpace(command)).ToArray();
            for (var index = 0; index < sequence.Length; index++)
            {
                var command = sequence[index];
                if (Core.Services.EditorCommandParser.TryParse(command, out var filePath))
                {
                    ResetInteractiveLine();
                    await OpenFileEditorAsync(_session, filePath, cancellationToken);
                }
                else
                {
                    ObserveShellCommand(command);
                    EchoLocalInputIfRequired(command + "\r");
                    await _session.SendAsync(command + "\r", cancellationToken);
                }

                await TryRecordAsync(command, source, cancellationToken);
                if (index + 1 < sequence.Length) await Task.Delay(180, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void EchoLocalInputIfRequired(string text)
    {
        if (!_session.RequiresLocalEcho || string.IsNullOrEmpty(text)) return;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            switch (character)
            {
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                    Buffer.Append("\r\n");
                    break;
                case '\n':
                    Buffer.Append("\r\n");
                    break;
                case '\b' or '\u007f':
                    Buffer.Append("\b \b");
                    break;
                case '\u0003':
                    Buffer.Append("^C\r\n");
                    break;
                case '\t':
                    Buffer.Append("\t");
                    break;
                case '\u001b':
                    // Arrow keys, Home/End and other VT sequences belong to the shell;
                    // they must not appear as printable characters in the local echo.
                    while (index + 1 < text.Length && !char.IsLetter(text[index + 1]) && text[index + 1] != '~') index++;
                    if (index + 1 < text.Length) index++;
                    break;
                default:
                    if (!char.IsControl(character)) Buffer.Append(character.ToString());
                    break;
            }
        }
    }

    protected override Task ReloadCoreAsync() => Task.CompletedTask;

    protected override void ObserveShellCommand(string command) =>
        _session.WorkingDirectory = ShellWorkingDirectory.Apply(_session.WorkingDirectory, command);

    public override async ValueTask DisposeAsync()
    {
        CloseFileEditor();
        await StopAsync();
        await _session.DisposeAsync();
    }

    private async Task StopAsync()
    {
        _session.OutputReceived -= OnOutputReceived;
        _session.Exited -= OnExited;
        if (_session.IsRunning) await _session.DisposeAsync();
        State = LocalTerminalState.Stopped;
    }

    private void OnOutputReceived(object? sender, string output) => Buffer.Append(output);

    private void OnExited(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            State = LocalTerminalState.Stopped;
            StatusMessage = "Локальный shell завершил работу. Откройте новую локальную вкладку для продолжения.";
        });
    }

    public enum LocalTerminalState
    {
        Stopped,
        Starting,
        Running
    }
}
