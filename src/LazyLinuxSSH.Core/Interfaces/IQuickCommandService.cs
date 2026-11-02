using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public interface IQuickCommandService
{
    IReadOnlyList<QuickCommandCategory> GetBuiltInCategories();
    Task<IReadOnlyList<CustomCommand>> GetCustomCommandsAsync(CancellationToken cancellationToken = default);
    Task<CustomCommand> SaveCustomCommandAsync(Guid? id, string name, IReadOnlyList<string> commands, CancellationToken cancellationToken = default);
    Task DeleteCustomCommandAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ICommandSequenceExecutor
{
    Task ExecuteAsync(ISshSession session, IEnumerable<string> commands, TimeSpan delay, CancellationToken cancellationToken = default);
}
