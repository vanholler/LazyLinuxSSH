using Microsoft.EntityFrameworkCore;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Infrastructure.Data;

public sealed class ConnectionRepository(IDbContextFactory<LazyLinuxDbContext> contextFactory) : IConnectionRepository
{
    public async Task<IReadOnlyList<ConnectionGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Groups
            .AsNoTracking()
            .Include(group => group.Connections)
            .OrderBy(group => group.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConnectionProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Profiles.AsNoTracking()
            .Include(profile => profile.Group)
            .SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
    }

    public async Task<ConnectionGroup> AddGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        if (normalized.Length is < 1 or > 100)
        {
            throw new ArgumentException("Название группы должно содержать от 1 до 100 символов.", nameof(name));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var lowered = normalized.ToLowerInvariant();
        if (await context.Groups.AnyAsync(group => group.Name.ToLower() == lowered, cancellationToken))
        {
            throw new InvalidOperationException("Группа с таким названием уже существует.");
        }

        var group = new ConnectionGroup { Name = normalized };
        await context.Groups.AddAsync(group, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return group;
    }

    public async Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Profiles.SingleOrDefaultAsync(item => item.Id == profile.Id, cancellationToken);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            await context.Profiles.AddAsync(profile, cancellationToken);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(profile);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Profiles.Where(profile => profile.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
