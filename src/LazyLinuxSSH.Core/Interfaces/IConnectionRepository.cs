using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public interface IConnectionRepository
{
    Task<IReadOnlyList<ConnectionGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<ConnectionProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConnectionGroup> AddGroupAsync(string name, CancellationToken cancellationToken = default);
    Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
