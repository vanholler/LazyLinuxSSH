using System.Collections.Concurrent;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Infrastructure.Security;
using LazyLinuxSSH.Infrastructure.Ssh;

namespace LazyLinuxSSH.Tests;

public sealed class SshSessionIntegrationTests
{
    [Fact]
    public async Task LocalSshd_PtyCommandOutputAndStaleState_WorkEndToEnd()
    {
        var keyPath = Environment.GetEnvironmentVariable("LAZYLINUXSSH_TEST_KEY");
        if (string.IsNullOrWhiteSpace(keyPath)) return; // Opt-in integration test; see README.

        var output = new ConcurrentQueue<string>();
        await using var protector = new CredentialProtector();
        var profile = new ConnectionProfile
        {
            Name = "Local integration",
            Host = "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("LAZYLINUXSSH_TEST_PORT") ?? "2222"),
            Username = Environment.GetEnvironmentVariable("LAZYLINUXSSH_TEST_USER") ?? Environment.UserName,
            AuthenticationMethod = AuthenticationMethod.PrivateKey,
            PrivateKeyPath = keyPath
        };
        await using var session = new SshSession(profile, protector);
        session.OutputReceived += (_, text) => output.Enqueue(text);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await session.ConnectAsync(timeout.Token);
        Assert.Equal(SshSessionState.Connected, session.State);
        await session.SendAsync("printf 'LAZYLINUXSSH_OK\\n'\n", timeout.Token);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline && !string.Join("", output).Contains("LAZYLINUXSSH_OK", StringComparison.Ordinal))
        {
            await Task.Delay(100, timeout.Token);
        }

        Assert.Contains("LAZYLINUXSSH_OK", string.Join("", output));
        Assert.True(await session.CheckHealthAsync(timeout.Token));

        var remotePath = $"/tmp/lazylinuxssh-editor-{Guid.NewGuid():N}.conf";
        var localPath = $"/tmp/lazylinuxssh-editor-download-{Guid.NewGuid():N}.conf";
        const string content = "[server]\nport = 8080\ncomment = Проверка SFTP\n";
        try
        {
            await session.WriteTextFileAsync(remotePath, content, timeout.Token);
            var file = await session.ReadTextFileAsync(remotePath, timeout.Token);
            Assert.Equal(remotePath, file.Path);
            Assert.True(file.Exists);
            Assert.Equal(content, file.Text);

            await session.DownloadFileAsync(remotePath, localPath, timeout.Token);
            Assert.Equal(content, await File.ReadAllTextAsync(localPath, timeout.Token));
        }
        finally
        {
            if (File.Exists(localPath)) File.Delete(localPath);
        }

        session.MarkStale();
        Assert.Equal(SshSessionState.Stale, session.State);
    }
}
