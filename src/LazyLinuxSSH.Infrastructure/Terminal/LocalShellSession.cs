using System.Diagnostics;
using System.Text;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Infrastructure.Files;

namespace LazyLinuxSSH.Infrastructure.Terminal;

public sealed class LocalShellSessionFactory : ILocalShellSessionFactory
{
    public ILocalShellSession CreateSession() => new LocalShellSession();
}

public sealed class LocalShellSession : ILocalShellSession
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly string? _fileName;
    private readonly string? _arguments;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task[] _readTasks = [];
    private bool _isRunning;
    private bool _isDisposed;

    public LocalShellSession(string? fileName = null, string? arguments = null)
    {
        _fileName = fileName;
        _arguments = arguments;
    }

    public string ShellName { get; private set; } = OperatingSystem.IsWindows() ? "cmd.exe" : "shell";
    public bool IsRunning => _isRunning && !_isDisposed;
    public bool RequiresLocalEcho => OperatingSystem.IsWindows();
    public string? WorkingDirectory { get; set; }
    public event EventHandler<string>? OutputReceived;
    public event EventHandler? Exited;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_process is not null) return;

        var (fileName, arguments) = ResolveShell();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
        {
            startInfo.WorkingDirectory = home;
            WorkingDirectory = home;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Операционная система не запустила локальный shell.");
            _process = process;
            ShellName = Path.GetFileName(fileName);
            _isRunning = true;
            _readTasks =
            [
                Task.Run(() => ReadOutputAsync(process.StandardOutput, _lifetime.Token), CancellationToken.None),
                Task.Run(() => ReadOutputAsync(process.StandardError, _lifetime.Token), CancellationToken.None)
            ];
            _ = MonitorExitAsync(process);

            if (OperatingSystem.IsWindows() && _fileName is null)
                await SendAsync("chcp 65001>nul\r\n", cancellationToken);
        }
        catch
        {
            _isRunning = false;
            process.Dispose();
            _process = null;
            throw;
        }
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text)) return;
        var process = _process;
        if (!IsRunning || process is null) throw new InvalidOperationException("Локальный shell не запущен.");

        var input = NormalizeLineEndings(text);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<TextFileContent> ReadTextFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveLocalPath(path);
        if (Directory.Exists(fullPath)) throw new InvalidDataException("Укажите файл, а не каталог.");
        if (!File.Exists(fullPath)) return new TextFileContent(fullPath, string.Empty, false);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
        return new TextFileContent(fullPath, await TextFileReader.ReadAsync(stream, cancellationToken), true);
    }

    public async Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveLocalPath(path);
        await File.WriteAllTextAsync(fullPath, content, Utf8NoBom, cancellationToken);
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveLocalPath(remotePath);
        var destinationPath = Path.GetFullPath(localPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(sourcePath, destinationPath, comparison)) return;

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public async Task UploadFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("Укажите локальный файл.", nameof(localPath));
        if (string.IsNullOrWhiteSpace(remoteFileName)) throw new ArgumentException("Укажите имя файла.", nameof(remoteFileName));
        if (!File.Exists(localPath)) throw new FileNotFoundException("Локальный файл не найден.", localPath);
        if (Directory.Exists(localPath)) throw new InvalidDataException("Можно загрузить только один файл, не каталог.");

        var safeName = Path.GetFileName(remoteFileName.Replace('\\', '/').TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
            throw new ArgumentException("Некорректное имя файла.", nameof(remoteFileName));

        var destinationPath = ResolveLocalPath(safeName);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(localPath), destinationPath, comparison)) return;

        await using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private string ResolveLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Укажите путь к файлу.", nameof(path));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~") return home;
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return Path.GetFullPath(Path.Combine(home, path[2..]));
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        var baseDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? home : WorkingDirectory;
        if (baseDirectory.StartsWith("~/", StringComparison.Ordinal) || baseDirectory.StartsWith("~\\", StringComparison.Ordinal))
            baseDirectory = Path.Combine(home, baseDirectory[2..]);
        else if (baseDirectory == "~")
            baseDirectory = home;

        return Path.GetFullPath(path, baseDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _isRunning = false;
        var process = _process;
        _process = null;
        if (process is not null)
        {
            try { process.StandardInput.Close(); } catch { /* Process may already have exited. */ }
            try
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await process.WaitForExitAsync(grace.Token);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch { /* Best-effort process cleanup. */ }
            }
            process.Dispose();
        }

        _lifetime.Cancel();
        if (_readTasks.Length > 0)
        {
            try { await Task.WhenAll(_readTasks); } catch { /* Output pipes close during shutdown. */ }
        }
        _lifetime.Dispose();
        _writeLock.Dispose();
    }

    private (string FileName, string Arguments) ResolveShell()
    {
        if (!string.IsNullOrWhiteSpace(_fileName)) return (_fileName, _arguments ?? string.Empty);
        if (OperatingSystem.IsWindows())
            return (Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe", _arguments ?? "/Q");

        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell))
            shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        return (shell, _arguments ?? "-i");
    }

    private async Task ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                OutputReceived?.Invoke(this, new string(buffer, 0, read));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Closing the shell or disposing the session closes both redirected pipes.
        }
    }

    private async Task MonitorExitAsync(Process process)
    {
        try { await process.WaitForExitAsync(_lifetime.Token); }
        catch (OperationCanceledException) { return; }
        catch (InvalidOperationException) { return; }

        _isRunning = false;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeLineEndings(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return OperatingSystem.IsWindows() ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal) : normalized;
    }
}
