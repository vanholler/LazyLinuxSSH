using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public interface ICommandTemplateService
{
    Task<IReadOnlyList<CommandTemplate>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CommandTemplate> SaveCustomAsync(
        Guid? id,
        string name,
        IReadOnlyCollection<string> selectedCommandKeys,
        CancellationToken cancellationToken = default);
}
