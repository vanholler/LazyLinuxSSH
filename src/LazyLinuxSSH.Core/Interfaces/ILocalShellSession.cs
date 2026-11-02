namespace LazyLinuxSSH.Core.Interfaces;

public interface ILocalShellSession : IFileTransferSession, IAsyncDisposable
{
    string ShellName { get; }
    bool IsRunning { get; }
    bool RequiresLocalEcho { get; }
    event EventHandler<string>? OutputReceived;
    event EventHandler? Exited;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SendAsync(string text, CancellationToken cancellationToken = default);
}

public interface ILocalShellSessionFactory
{
    ILocalShellSession CreateSession();
}
