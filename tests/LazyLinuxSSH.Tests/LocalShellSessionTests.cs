using System.Collections.Concurrent;
using LazyLinuxSSH.Infrastructure.Terminal;

namespace LazyLinuxSSH.Tests;

public sealed class LocalShellSessionTests
{
    [Fact]
    public async Task EmbeddedShell_AcceptsInput_AndReturnsOutputWithoutExternalWindow()
    {
        var fileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows() ? "/Q" : string.Empty;
        var output = new ConcurrentQueue<string>();
        await using var session = new LocalShellSession(fileName, arguments);
        session.OutputReceived += (_, text) => output.Enqueue(text);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await session.StartAsync(timeout.Token);
        Assert.True(session.IsRunning);
        await session.SendAsync(
            OperatingSystem.IsWindows() ? "echo LAZYLINUXSSH_LOCAL_OK\r" : "printf 'LAZYLINUXSSH_LOCAL_OK\\n'\n",
            timeout.Token);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTimeOffset.UtcNow < deadline &&
               !string.Join("", output).Contains("LAZYLINUXSSH_LOCAL_OK", StringComparison.Ordinal))
        {
            await Task.Delay(80, timeout.Token);
        }

        Assert.Contains("LAZYLINUXSSH_LOCAL_OK", string.Join("", output));
    }
}
