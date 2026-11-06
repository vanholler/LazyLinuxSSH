using System.Text;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Infrastructure.Files;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshAuthenticationMethod = Renci.SshNet.AuthenticationMethod;

namespace LazyLinuxSSH.Infrastructure.Ssh;

public sealed class SshSession : ISshSession
{
    private readonly ICredentialProtector _credentialProtector;
    private readonly List<IDisposable> _keyFiles = [];
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _pwdSync = new();
    private readonly StringBuilder _pwdAccumulator = new();
    private CancellationTokenSource? _lifetime;
    private SshClient? _client;
    private ShellStream? _shell;
    private Task? _readTask;
    private SshSessionState _state = SshSessionState.Disconnected;
    private bool _disposed;
    private TaskCompletionSource<string>? _pwdWaiter;
    private string? _pwdMarker;

    public SshSession(ConnectionProfile profile, ICredentialProtector credentialProtector)
    {
        Profile = profile;
        _credentialProtector = credentialProtector;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public ConnectionProfile Profile { get; }
    public SshSessionState State => _state;
    public DateTimeOffset? LastOutputAt { get; private set; }
    public string? WorkingDirectory { get; set; }
    public event EventHandler<string>? OutputReceived;
    public event EventHandler<SshSessionState>? StateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state is SshSessionState.Connected or SshSessionState.Connecting) return;
        SetState(SshSessionState.Connecting);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var authentication = await CreateAuthenticationMethodsAsync(cancellationToken);
            var connection = new ConnectionInfo(Profile.Host, Profile.Port, Profile.Username, authentication)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _client = new SshClient(connection) { KeepAliveInterval = TimeSpan.FromSeconds(15) };
            await _client.ConnectAsync(_lifetime.Token);
            _shell = _client.CreateShellStream("xterm-256color", 200, 40, 1600, 900, 8192);
            SetState(SshSessionState.Connected);
            _readTask = Task.Run(() => ReadLoopAsync(_lifetime.Token), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetState(SshSessionState.Faulted);
            OutputReceived?.Invoke(this, $"\r\n[Ошибка SSH: {exception.Message}]\r\n");
            throw;
        }
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state != SshSessionState.Connected || _shell is null)
            throw new InvalidOperationException("SSH-сессия не подключена.");

        var bytes = Encoding.UTF8.GetBytes(text);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _shell.WriteAsync(bytes.AsMemory(), cancellationToken);
            await _shell.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MarkStale();
            throw new IOException("Не удалось отправить данные в SSH-сессию.", exception);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<TextFileContent> ReadTextFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var client = await CreateConnectedSftpClientAsync(cancellationToken);
        var fullPath = await ResolveRemotePathAsync(client, path, cancellationToken);
        try
        {
            var attributes = await client.GetAttributesAsync(fullPath, cancellationToken);
            if (attributes is null) return new TextFileContent(fullPath, string.Empty, false);
            if (attributes.IsDirectory) throw new InvalidDataException("Укажите файл, а не каталог.");
            if (attributes.Size > TextFileReader.MaxTextFileBytes)
                throw new InvalidDataException("Файл больше 16 МБ. Для безопасной работы редактор открывает только текстовые файлы до 16 МБ.");

            await using var memory = new MemoryStream();
            await client.DownloadFileAsync(fullPath, memory, cancellationToken);
            memory.Position = 0;
            return new TextFileContent(fullPath, await TextFileReader.ReadAsync(memory, cancellationToken), true);
        }
        catch (SftpPathNotFoundException)
        {
            return new TextFileContent(fullPath, string.Empty, false);
        }
    }

    public async Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var client = await CreateConnectedSftpClientAsync(cancellationToken);
        var fullPath = await ResolveRemotePathAsync(client, path, cancellationToken);
        await using var stream = TextFileReader.CreateUtf8Stream(content);
        await client.UploadFileAsync(stream, fullPath, cancellationToken);
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("Выберите место сохранения.", nameof(localPath));
        using var client = await CreateConnectedSftpClientAsync(cancellationToken);
        var fullPath = await ResolveRemotePathAsync(client, remotePath, cancellationToken);
        await using var destination = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await client.DownloadFileAsync(fullPath, destination, cancellationToken);
    }

    public async Task UploadFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("Укажите локальный файл.", nameof(localPath));
        if (string.IsNullOrWhiteSpace(remoteFileName)) throw new ArgumentException("Укажите имя файла на сервере.", nameof(remoteFileName));
        if (!File.Exists(localPath)) throw new FileNotFoundException("Локальный файл не найден.", localPath);
        if (Directory.Exists(localPath)) throw new InvalidDataException("Можно загрузить только один файл, не каталог.");

        var safeName = Path.GetFileName(remoteFileName.Replace('\\', '/').TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
            throw new ArgumentException("Некорректное имя файла на сервере.", nameof(remoteFileName));

        using var client = await CreateConnectedSftpClientAsync(cancellationToken);
        var fullPath = await ResolveRemotePathAsync(client, safeName, cancellationToken);
        await using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await client.UploadFileAsync(source, fullPath, cancellationToken);
    }

    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_state != SshSessionState.Connected) return Task.FromResult(false);
        if (_client?.IsConnected == true) return Task.FromResult(true);
        MarkStale();
        return Task.FromResult(false);
    }

    public void MarkStale()
    {
        if (!_disposed && _state is SshSessionState.Connected or SshSessionState.Connecting)
        {
            SetState(SshSessionState.Stale);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_lifetime is not null) await _lifetime.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed during a reconnect race.
        }

        try
        {
            if (_client?.IsConnected == true) _client.Disconnect();
        }
        catch
        {
            // Disposal must remain best-effort; the UI is already replacing the session.
        }

        _shell?.Dispose();
        _client?.Dispose();
        foreach (var keyFile in _keyFiles) keyFile.Dispose();
        _keyFiles.Clear();
        _lifetime?.Dispose();
        _writeLock.Dispose();
        SetState(SshSessionState.Disconnected);
    }

    private async Task<SftpClient> CreateConnectedSftpClientAsync(CancellationToken cancellationToken)
    {
        var authentication = await CreateAuthenticationMethodsAsync(cancellationToken);
        var connection = new ConnectionInfo(Profile.Host, Profile.Port, Profile.Username, authentication)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        var client = new SftpClient(connection);
        try
        {
            await client.ConnectAsync(cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<string> ResolveRemotePathAsync(
        SftpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        var home = (await client.GetAsync(".", cancellationToken)).FullName.TrimEnd('/');
        if (NeedsShellWorkingDirectory(path))
            await RefreshWorkingDirectoryFromShellAsync(cancellationToken);
        return ShellWorkingDirectory.ResolveAgainstHome(WorkingDirectory, home, path);
    }

    private static bool NeedsShellWorkingDirectory(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path != "~" &&
        !path.StartsWith('/') &&
        !path.StartsWith("~/") &&
        !path.StartsWith("~\\");

    /// <summary>
    /// Asks the interactive PTY for the real cwd. Tracking <c>cd</c> alone is not enough
    /// when the user reaches a folder another way or the local tracker drifts.
    /// </summary>
    private async Task RefreshWorkingDirectoryFromShellAsync(CancellationToken cancellationToken)
    {
        if (_shell is null || _state != SshSessionState.Connected) return;

        var marker = $"__LLSSH_PWD_{Guid.NewGuid():N}__";
        var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pwdSync)
        {
            _pwdMarker = marker;
            _pwdWaiter = waiter;
            _pwdAccumulator.Clear();
        }

        // Clear any partial input, then print pwd between unique markers (hidden from the UI).
        var probe = $"\u0015printf '%s%s%s\\n' '{marker}' \"$(pwd -P 2>/dev/null || pwd)\" '{marker}'\r";
        try
        {
            await SendAsync(probe, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var pwd = (await waiter.Task.WaitAsync(timeout.Token)).Trim().TrimEnd('\r', '\n');
            if (pwd.StartsWith('/'))
                WorkingDirectory = pwd;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out — keep the last known WorkingDirectory / fall back to home.
        }
        catch
        {
            // Best-effort; relative paths still fall back to home.
        }
        finally
        {
            lock (_pwdSync)
            {
                if (ReferenceEquals(_pwdWaiter, waiter))
                {
                    _pwdWaiter = null;
                    _pwdMarker = null;
                    _pwdAccumulator.Clear();
                }
            }
        }
    }

    private string FilterPwdProbeOutput(string text)
    {
        lock (_pwdSync)
        {
            if (_pwdWaiter is null || _pwdMarker is null) return text;

            _pwdAccumulator.Append(text);
            var buffer = _pwdAccumulator.ToString();
            var marker = _pwdMarker;
            var start = buffer.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                // Hold output while the probe is active so the printf echo never flashes.
                return string.Empty;
            }

            var afterStart = start + marker.Length;
            var end = buffer.IndexOf(marker, afterStart, StringComparison.Ordinal);
            if (end < 0)
            {
                _pwdAccumulator.Clear();
                _pwdAccumulator.Append(buffer[start..]);
                return string.Empty;
            }

            var pwd = buffer[afterStart..end].Trim().Trim('\r', '\n');
            var remainder = buffer[(end + marker.Length)..];
            // Drop a leading newline left after the marker line.
            if (remainder.StartsWith('\r')) remainder = remainder[1..];
            if (remainder.StartsWith('\n')) remainder = remainder[1..];

            var waiter = _pwdWaiter;
            _pwdWaiter = null;
            _pwdMarker = null;
            _pwdAccumulator.Clear();
            waiter.TrySetResult(pwd);
            return remainder;
        }
    }

    private async Task<SshAuthenticationMethod[]> CreateAuthenticationMethodsAsync(CancellationToken cancellationToken)
    {
        if (Profile.AuthenticationMethod == Core.Models.AuthenticationMethod.Password)
        {
            var password = await _credentialProtector.UnprotectAsync(Profile.EncryptedPassword, cancellationToken);
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Для профиля не сохранён пароль.");
            return [new PasswordAuthenticationMethod(Profile.Username, password)];
        }

        if (string.IsNullOrWhiteSpace(Profile.PrivateKeyPath))
            throw new InvalidOperationException("Укажите путь к закрытому ключу.");
        var passphrase = await _credentialProtector.UnprotectAsync(Profile.EncryptedPassphrase, cancellationToken);
        var keyFile = string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(Profile.PrivateKeyPath)
            : new PrivateKeyFile(Profile.PrivateKeyPath, passphrase);
        _keyFiles.Add(keyFile);
        return [new PrivateKeyAuthenticationMethod(Profile.Username, keyFile)];
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_shell is null) return;
        var buffer = new byte[8192];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var decoder = Encoding.UTF8.GetDecoder();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _shell.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    MarkStale();
                    break;
                }

                // Decoder preserves a multibyte UTF-8 character split across two reads.
                decoder.Convert(buffer, 0, read, characters, 0, characters.Length, false,
                    out _, out var charsUsed, out _);
                if (charsUsed > 0)
                {
                    var chunk = FilterPwdProbeOutput(new string(characters, 0, charsUsed));
                    if (chunk.Length == 0) continue;
                    LastOutputAt = DateTimeOffset.UtcNow;
                    OutputReceived?.Invoke(this, chunk);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during explicit close/reconnect.
        }
        catch (Exception exception)
        {
            MarkStale();
            OutputReceived?.Invoke(this, $"\r\n[Соединение потеряно: {exception.Message}]\r\n");
        }
    }

    private void SetState(SshSessionState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }
}
