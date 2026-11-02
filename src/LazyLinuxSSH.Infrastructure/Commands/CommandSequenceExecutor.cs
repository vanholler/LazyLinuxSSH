using LazyLinuxSSH.Core.Interfaces;

namespace LazyLinuxSSH.Infrastructure.Commands;

public sealed class CommandSequenceExecutor : ICommandSequenceExecutor
{
    public async Task ExecuteAsync(
        ISshSession session,
        IEnumerable<string> commands,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        var sequence = commands.Where(command => !string.IsNullOrWhiteSpace(command)).ToArray();
        for (var index = 0; index < sequence.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.SendAsync(sequence[index] + "\n", cancellationToken);
            if (index < sequence.Length - 1)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
