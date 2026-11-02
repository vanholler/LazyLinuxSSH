using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class TerminalSessionViewModel : TerminalViewModelBase
{
    private readonly ISshSessionManager _sessionManager;
    private readonly ICommandSequenceExecutor _sequenceExecutor;
    private ISshSession? _session;

    public TerminalSessionViewModel(
        ConnectionProfile profile,
        ISshSessionManager sessionManager,
        ICommandSequenceExecutor sequenceExecutor,
        ICommandHistoryService historyService,
        IClipboardService clipboardService,
        IFileSaveDialogService fileSaveDialogService,
        Func<TerminalViewModelBase, Task> closeRequested,
        Action<TerminalViewModelBase> activated)
        : base(historyService, clipboardService, fileSaveDialogService, closeRequested, activated)
    {
        Profile = profile;
        Title = profile.Name.Length <= 22 ? profile.Name : $"{profile.Name[..21]}…";
        _sessionManager = sessionManager;
        _sequenceExecutor = sequenceExecutor;
        StatusMessage = $"{profile.Username}@{profile.Host}:{profile.Port}";
    }

    public ConnectionProfile Profile { get; }
    public override bool IsConnected => State == SshSessionState.Connected;
    public override bool IsReconnectOverlayVisible => State is SshSessionState.Stale or SshSessionState.Faulted;
    public override string StateLabel => State switch
    {
        SshSessionState.Connecting => "Подключение",
        SshSessionState.Connected => "В сети",
        SshSessionState.Stale => "Разорвано",
        SshSessionState.Faulted => "Ошибка",
        _ => "Отключено"
    };
    protected override string HistoryServerName => Profile.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsReconnectOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private SshSessionState _state = SshSessionState.Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_session is not null) return;
        IsBusy = true;
        State = SshSessionState.Connecting;
        StatusMessage = "Устанавливаем SSH-соединение…";
        try
        {
            _session = _sessionManager.CreateSession(Profile);
            _session.OutputReceived += OnOutputReceived;
            _session.StateChanged += OnSessionStateChanged;
            await _session.ConnectAsync(cancellationToken);
            State = _session.State;
            StatusMessage = $"{Profile.Username}@{Profile.Host}:{Profile.Port}";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_session is null || State != SshSessionState.Connected)
        {
            StatusMessage = "Нет активного SSH-соединения.";
            return;
        }

        try
        {
            var editorCommand = GetCompletedEditorCommand(text);
            if (editorCommand is not null && Core.Services.EditorCommandParser.TryParse(editorCommand, out var filePath))
            {
                ResetInteractiveLine();
                await _session.SendAsync("\u0015\r", cancellationToken);
                await TryRecordAsync(editorCommand, CommandHistorySource.Interactive, cancellationToken);
                await OpenFileEditorAsync(_session, filePath, cancellationToken);
                return;
            }

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
        if (_session is null || State != SshSessionState.Connected)
        {
            StatusMessage = "Сначала подключите терминал.";
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
                    await _sequenceExecutor.ExecuteAsync(_session, [command], TimeSpan.Zero, cancellationToken);
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

    protected override async Task ReloadCoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            CloseFileEditor();
            await DetachAndDisposeSessionAsync();
            Buffer.Append("\r\n[Переподключение…]\r\n");
        }
        finally
        {
            IsBusy = false;
        }

        await ConnectAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        CloseFileEditor();
        await DetachAndDisposeSessionAsync();
    }

    private void OnOutputReceived(object? sender, string output) => Buffer.Append(output);

    private void OnSessionStateChanged(object? sender, SshSessionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            State = state;
            if (state == SshSessionState.Stale) StatusMessage = "Соединение потеряно. Можно переподключиться.";
        });
    }

    private async Task DetachAndDisposeSessionAsync()
    {
        if (_session is null) return;
        var session = _session;
        _session = null;
        session.OutputReceived -= OnOutputReceived;
        session.StateChanged -= OnSessionStateChanged;
        await _sessionManager.CloseSessionAsync(session);
        State = SshSessionState.Disconnected;
    }
}
