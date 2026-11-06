using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Core.Terminal;

namespace LazyLinuxSSH.Desktop.ViewModels;

public abstract partial class TerminalViewModelBase : ObservableObject, IAsyncDisposable
{
    private readonly ICommandHistoryService _historyService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSaveDialogService _fileSaveDialogService;
    private readonly Func<TerminalViewModelBase, Task> _closeRequested;
    private readonly Action<TerminalViewModelBase> _activated;
    private readonly StringBuilder _interactiveLine = new();
    private bool _isSecretPrompt;

    protected TerminalViewModelBase(
        ICommandHistoryService historyService,
        IClipboardService clipboardService,
        IFileSaveDialogService fileSaveDialogService,
        Func<TerminalViewModelBase, Task> closeRequested,
        Action<TerminalViewModelBase> activated)
    {
        _historyService = historyService;
        _clipboardService = clipboardService;
        _fileSaveDialogService = fileSaveDialogService;
        _closeRequested = closeRequested;
        _activated = activated;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public AnsiTerminalBuffer Buffer { get; } = new();
    public abstract bool IsConnected { get; }
    public virtual bool IsReconnectOverlayVisible => false;
    public abstract string StateLabel { get; }
    public bool IsFileEditorOpen => FileEditor is not null;
    public virtual bool CanAcceptFileDrop => false;
    public virtual bool IsFileDropOverlayVisible => false;
    public virtual bool IsUploadInProgress => false;
    public virtual string FileDropBannerText => string.Empty;
    protected abstract string HistoryServerName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileEditorOpen))]
    [NotifyPropertyChangedFor(nameof(CanAcceptFileDrop))]
    private FileEditorViewModel? _fileEditor;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isBusy;

    public abstract Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    public abstract Task ExecuteCommandsAsync(
        IEnumerable<string> commands,
        CommandHistorySource source = CommandHistorySource.QuickCommand,
        CancellationToken cancellationToken = default);

    [RelayCommand]
    private void Activate() => _activated(this);

    [RelayCommand]
    private Task Close() => _closeRequested(this);

    [RelayCommand]
    private void Clear() => Buffer.Clear();

    [RelayCommand]
    private Task Reload() => ReloadCoreAsync();

    protected virtual Task ReloadCoreAsync() => Task.CompletedTask;

    /// <summary>
    /// On Enter, prefer the line readline is showing (tab completion, history arrows)
    /// over the raw keystroke buffer, and take the directory from the prompt.
    /// </summary>
    protected void SyncInteractiveLineFromScreen(string input)
    {
        if (input is not ("\r" or "\n" or "\r\n")) return;
        if (!ShellPrompt.TryReadInput(Buffer.GetCursorLine(), out var directory, out var command)) return;
        NotePromptDirectory(directory);
        _interactiveLine.Clear();
        _interactiveLine.Append(command);
    }

    protected virtual void NotePromptDirectory(string? directory)
    {
    }

    protected string? GetCompletedEditorCommand(string input)
    {
        string? candidate = null;
        if (input is "\r" or "\n" or "\r\n")
        {
            candidate = _interactiveLine.ToString();
        }
        else if (_interactiveLine.Length == 0)
        {
            var lineEnding = input.IndexOfAny(['\r', '\n']);
            if (lineEnding > 0 && input[lineEnding..].All(character => character is '\r' or '\n'))
                candidate = input[..lineEnding];
        }

        return candidate is not null && EditorCommandParser.TryParse(candidate, out _) ? candidate : null;
    }

    protected void ResetInteractiveLine()
    {
        _interactiveLine.Clear();
        _isSecretPrompt = false;
    }

    /// <summary>
    /// Ctrl+U clears the remote echo of an intercepted editor command; reprint it locally
    /// so the user still sees what they typed (e.g. nano .env).
    /// </summary>
    protected void EchoInterceptedCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var text = command.TrimEnd('\r', '\n');
        Buffer.Append("\r\n" + text + "\r\n");
    }

    protected async Task OpenFileEditorAsync(
        IFileTransferSession session,
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            StatusMessage = "Укажите один файл, например: nano /etc/hosts";
            return;
        }

        CloseFileEditor();
        var editor = new FileEditorViewModel(
            session,
            _clipboardService,
            _fileSaveDialogService,
            filePath,
            CloseFileEditor);
        FileEditor = editor;
        StatusMessage = $"Открытие {filePath}…";
        await editor.LoadAsync();
        StatusMessage = editor.StatusMessage;
    }

    protected void CloseFileEditor(FileEditorViewModel? editor = null)
    {
        if (editor is not null && !ReferenceEquals(editor, FileEditor)) return;
        var current = FileEditor;
        FileEditor = null;
        current?.Dispose();
        if (current is not null) StatusMessage = current.StatusMessage;
    }

    protected async Task CaptureInteractiveInputAsync(string text, CancellationToken cancellationToken)
    {
        if (text.StartsWith('\u001b')) return;
        if (_interactiveLine.Length == 0) _isSecretPrompt = IsSecretPrompt();

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '\r' or '\n')
            {
                if (character == '\n' && index > 0 && text[index - 1] == '\r') continue;
                if (!_isSecretPrompt && _interactiveLine.Length > 0)
                {
                    var command = _interactiveLine.ToString();
                    ObserveShellCommand(command);
                    await TryRecordAsync(command, CommandHistorySource.Interactive, cancellationToken);
                }
                _interactiveLine.Clear();
                _isSecretPrompt = false;
                continue;
            }

            if (character is '\u0003' or '\u0004' or '\u0015')
            {
                _interactiveLine.Clear();
                continue;
            }

            if (character is '\b' or '\u007f')
            {
                if (!_isSecretPrompt && _interactiveLine.Length > 0) _interactiveLine.Length--;
                continue;
            }

            if (!char.IsControl(character) && !_isSecretPrompt) _interactiveLine.Append(character);
        }
    }

    protected async Task TryRecordAsync(
        string command,
        CommandHistorySource source,
        CancellationToken cancellationToken)
    {
        try
        {
            await _historyService.AddAsync(command, HistoryServerName, source, cancellationToken);
        }
        catch
        {
            // A history database problem must never interrupt an active terminal.
        }
    }

    protected virtual void ObserveShellCommand(string command)
    {
    }

    private bool IsSecretPrompt()
    {
        var snapshot = Buffer.GetSnapshot().Text;
        var lastLine = snapshot.Split('\n').LastOrDefault() ?? string.Empty;
        return lastLine.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               lastLine.Contains("passphrase", StringComparison.OrdinalIgnoreCase) ||
               lastLine.Contains("парол", StringComparison.OrdinalIgnoreCase);
    }

    public abstract ValueTask DisposeAsync();
}
