using System.Collections.Concurrent;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Infrastructure.Ssh;

public sealed class SshSessionManager : ISshSessionManager
{
    private readonly ICredentialProtector _credentialProtector;
    private readonly ISessionHealthMonitor _healthMonitor;
    private readonly ConcurrentDictionary<Guid, ISshSession> _sessions = new();

    public SshSessionManager(ICredentialProtector credentialProtector, ISessionHealthMonitor healthMonitor)
    {
        _credentialProtector = credentialProtector;
        _healthMonitor = healthMonitor;
    }

    public ISshSession CreateSession(ConnectionProfile profile)
    {
        var session = new SshSession(profile, _credentialProtector);
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException("Не удалось зарегистрировать SSH-сессию.");
        _healthMonitor.Register(session);
        return session;
    }

    public async Task CloseSessionAsync(ISshSession session)
    {
        _sessions.TryRemove(session.Id, out _);
        _healthMonitor.Unregister(session);
        await session.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var sessions = _sessions.Values.ToArray();
        _sessions.Clear();
        foreach (var session in sessions)
        {
            _healthMonitor.Unregister(session);
            await session.DisposeAsync();
        }
    }
}

public sealed class SessionHealthMonitor : ISessionHealthMonitor
{
    private readonly ConcurrentDictionary<Guid, ISshSession> _sessions = new();
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cancellation;
    private Task? _monitorTask;

    public void Register(ISshSession session)
    {
        _sessions[session.Id] = session;
        lock (_lifecycleLock)
        {
            if (_monitorTask is not null) return;
            _cancellation = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorAsync(_cancellation.Token));
        }
    }

    public void Unregister(ISshSession session) => _sessions.TryRemove(session.Id, out _);

    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (_lifecycleLock)
        {
            if (_cancellation is null) return;
            _cancellation.Cancel();
            task = _monitorTask;
            _monitorTask = null;
        }

        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }

        _cancellation?.Dispose();
        _sessions.Clear();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            foreach (var session in _sessions.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await session.CheckHealthAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch { session.MarkStale(); }
            }
        }
    }
}
