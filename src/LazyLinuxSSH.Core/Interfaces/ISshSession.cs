using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public enum SshSessionState
{
    Disconnected,
    Connecting,
    Connected,
    Stale,
    Faulted
}

public interface ISshSession : IFileTransferSession, IAsyncDisposable
{
    Guid Id { get; }
    ConnectionProfile Profile { get; }
    SshSessionState State { get; }
    DateTimeOffset? LastOutputAt { get; }
    event EventHandler<string>? OutputReceived;
    event EventHandler<SshSessionState>? StateChanged;
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task SendAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    void MarkStale();
}

public interface ISshSessionManager : IAsyncDisposable
{
    ISshSession CreateSession(ConnectionProfile profile);
    Task CloseSessionAsync(ISshSession session);
}

public interface ISessionHealthMonitor : IAsyncDisposable
{
    void Register(ISshSession session);
    void Unregister(ISshSession session);
}
