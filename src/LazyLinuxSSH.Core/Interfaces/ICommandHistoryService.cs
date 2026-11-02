using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public interface ICommandHistoryService
{
    Task AddAsync(
        string commandText,
        string? serverName,
        CommandHistorySource source,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandHistoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
