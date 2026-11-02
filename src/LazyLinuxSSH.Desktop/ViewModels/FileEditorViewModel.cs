using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LazyLinuxSSH.Core.Interfaces;

namespace LazyLinuxSSH.Desktop.ViewModels;

public sealed partial class FileEditorViewModel : ObservableObject, IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly IFileTransferSession _session;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSaveDialogService _fileSaveDialogService;
    private readonly Action<FileEditorViewModel> _close;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _suppressDirty;
    private bool _disposed;

    public FileEditorViewModel(
        IFileTransferSession session,
        IClipboardService clipboardService,
        IFileSaveDialogService fileSaveDialogService,
        string filePath,
        Action<FileEditorViewModel> close)
    {
        _session = session;
        _clipboardService = clipboardService;
        _fileSaveDialogService = fileSaveDialogService;
        _close = close;
        _filePath = filePath;
        _fileName = GetFileName(filePath);
        _statusMessage = "Загрузка файла…";
        _isBusy = true;
    }

    public string DisplayName => IsDirty ? $"● {FileName}" : FileName;
    public string DownloadIcon => IsDownloaded ? "✓" : "⇩";
    public string DownloadToolTip => IsDownloaded ? "Файл скачан" : "Скачать текущее содержимое";
    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _fileName;

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _statusMessage;
    [ObservableProperty] private string _caretLabel = "Строка 1, столбец 1";
    [ObservableProperty] private string _documentLabel = "1 строка · UTF-8";
    [ObservableProperty] private bool _exists;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isDirty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadIcon))]
    [NotifyPropertyChangedFor(nameof(DownloadToolTip))]
    private bool _isDownloaded;

    public async Task LoadAsync()
    {
        try
        {
            var file = await _session.ReadTextFileAsync(FilePath, _lifetime.Token);
            ObjectDisposedException.ThrowIf(_disposed, this);
            FilePath = file.Path;
            FileName = GetFileName(file.Path);
            Exists = file.Exists;
            _suppressDirty = true;
            Content = file.Text;
            _suppressDirty = false;
            IsDirty = false;
            DocumentLabel = $"{CountLines(file.Text)} {FormatLines(CountLines(file.Text))} · UTF-8";
            StatusMessage = file.Exists ? "Файл открыт. Ctrl+S — сохранить." : "Новый файл. Ctrl+S — создать на сервере.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The overlay was closed while the file was loading.
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось открыть файл: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try
        {
            await _session.WriteTextFileAsync(FilePath, Content, _lifetime.Token);
            Exists = true;
            IsDirty = false;
            StatusMessage = "Сохранено.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось сохранить: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (IsBusy) return;
        try
        {
            await _clipboardService.SetTextAsync(Content, _lifetime.Token);
            StatusMessage = "Всё содержимое файла скопировано.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось скопировать: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task Download()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try
        {
            var localPath = await _fileSaveDialogService.PickSavePathAsync(FileName, _lifetime.Token);
            if (string.IsNullOrWhiteSpace(localPath))
            {
                StatusMessage = "Скачивание отменено.";
                return;
            }

            if (IsDirty || !Exists)
                await File.WriteAllTextAsync(localPath, Content, Utf8NoBom, _lifetime.Token);
            else
                await _session.DownloadFileAsync(FilePath, localPath, _lifetime.Token);

            IsDownloaded = true;
            StatusMessage = $"Скачано: {localPath}";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            IsDownloaded = false;
            StatusMessage = $"Не удалось скачать: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close() => _close(this);

    public void UpdateDocumentState(int line, int column, int totalLines, int selectionLength)
    {
        CaretLabel = selectionLength > 0
            ? $"Строка {line}, столбец {column} · выделено {selectionLength}"
            : $"Строка {line}, столбец {column}";
        DocumentLabel = $"{totalLines} {FormatLines(totalLines)} · UTF-8";
    }

    partial void OnContentChanged(string value)
    {
        if (_suppressDirty) return;
        IsDirty = true;
        IsDownloaded = false;
        StatusMessage = "Есть несохранённые изменения.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private static string GetFileName(string path) =>
        path.Replace('\\', '/').TrimEnd('/').Split('/').LastOrDefault() is { Length: > 0 } name ? name : path;

    private static int CountLines(string text) => text.Count(character => character == '\n') + 1;

    private static string FormatLines(int count)
    {
        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14) return "строк";
        return (count % 10) switch
        {
            1 => "строка",
            2 or 3 or 4 => "строки",
            _ => "строк"
        };
    }
}
