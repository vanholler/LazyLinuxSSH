using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Desktop.Services;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class TerminalSessionViewModel : TerminalViewModelBase
{
    private readonly ISshSessionManager _sessionManager;
    private readonly ICommandSequenceExecutor _sequenceExecutor;
    private ISshSession? _session;
    private CancellationTokenSource? _uploadLifetime;

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
    public override bool CanAcceptFileDrop =>
        IsConnected && !IsFileEditorOpen && !IsUploadInProgress && !IsReconnectOverlayVisible;
    public override bool IsFileDropOverlayVisible => IsDropTargetVisible || IsUploadInProgress;
    public override string FileDropBannerText => IsUploadInProgress
        ? AppText.T("Загрузка файла…")
        : AppText.T("Залить файл в текущую директорию");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsReconnectOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(CanAcceptFileDrop))]
    private SshSessionState _state = SshSessionState.Disconnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileDropOverlayVisible))]
    private bool _isDropTargetVisible;

    private bool _isUploadInProgress;
    public override bool IsUploadInProgress => _isUploadInProgress;

    private void SetUploadInProgress(bool value)
    {
        if (!SetProperty(ref _isUploadInProgress, value, nameof(IsUploadInProgress))) return;
        OnPropertyChanged(nameof(IsFileDropOverlayVisible));
        OnPropertyChanged(nameof(FileDropBannerText));
        OnPropertyChanged(nameof(CanAcceptFileDrop));
    }

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
            SyncInteractiveLineFromScreen(text);
            var editorCommand = GetCompletedEditorCommand(text);
            if (editorCommand is not null && Core.Services.EditorCommandParser.TryParse(editorCommand, out var filePath))
            {
                ResetInteractiveLine();
                // Kill the remote line so nano/vim does not start and nothing is written to shell history.
                await _session.SendAsync("\u0015", cancellationToken);
                await Task.Delay(40, cancellationToken);
                EchoInterceptedCommand(editorCommand);
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
                    ObserveShellCommand(command);
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
        _uploadLifetime?.Cancel();
        _uploadLifetime?.Dispose();
        _uploadLifetime = null;
        CloseFileEditor();
        await DetachAndDisposeSessionAsync();
    }

    protected override void ObserveShellCommand(string command)
    {
        if (_session is null) return;
        _session.WorkingDirectory = ShellWorkingDirectory.Apply(_session.WorkingDirectory, command);
    }

    protected override void NotePromptDirectory(string? directory)
    {
        if (_session is null || string.IsNullOrWhiteSpace(directory)) return;
        if (directory.StartsWith('/') || directory == "~" || directory.StartsWith("~/"))
            _session.WorkingDirectory = directory;
    }

    public void SetDropTargetVisible(bool visible)
    {
        if (IsUploadInProgress) return;
        IsDropTargetVisible = visible && CanAcceptFileDrop;
    }

    public async Task UploadDroppedFileAsync(string localPath, CancellationToken cancellationToken = default)
    {
        IsDropTargetVisible = false;
        if (_session is null || !CanAcceptFileDrop)
        {
            StatusMessage = "Нет активного SSH-соединения для загрузки файла.";
            return;
        }

        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath) || Directory.Exists(localPath))
        {
            StatusMessage = "Можно загрузить только один обычный файл.";
            return;
        }

        var fileName = Path.GetFileName(localPath);
        _uploadLifetime?.Cancel();
        _uploadLifetime?.Dispose();
        _uploadLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _uploadLifetime.Token;

        SetUploadInProgress(true);
        StatusMessage = $"Загрузка «{fileName}»…";
        try
        {
            await _session.UploadFileAsync(localPath, fileName, token);
            StatusMessage = $"Файл «{fileName}» загружен в текущую директорию.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusMessage = "Загрузка файла отменена.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось загрузить файл: {exception.Message}";
        }
        finally
        {
            SetUploadInProgress(false);
            IsDropTargetVisible = false;
        }
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
