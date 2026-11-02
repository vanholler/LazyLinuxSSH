using System.Text.RegularExpressions;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LazyLinuxSSH.Infrastructure.Data;

public sealed partial class CommandHistoryService(IDbContextFactory<LazyLinuxDbContext> contextFactory) : ICommandHistoryService
{
    public async Task AddAsync(
        string commandText,
        string? serverName,
        CommandHistorySource source,
        CancellationToken cancellationToken = default)
    {
        var sanitized = Sanitize(commandText);
        if (string.IsNullOrWhiteSpace(sanitized)) return;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.CommandHistory.AddAsync(new CommandHistoryEntry
        {
            CommandText = sanitized,
            ServerName = string.IsNullOrWhiteSpace(serverName) ? null : serverName.Trim(),
            Source = source,
            ExecutedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CommandHistoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entries = await context.CommandHistory.AsNoTracking().ToListAsync(cancellationToken);
        return entries
            .OrderByDescending(entry => entry.ExecutedAt)
            .ThenByDescending(entry => entry.Id)
            .ToList();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.CommandHistory.ExecuteDeleteAsync(cancellationToken);
    }

    internal static string Sanitize(string commandText)
    {
        var value = commandText.Trim();
        if (value.Length > 8000) value = value[..8000];
        value = InlineSecretRegex().Replace(value, "$1[СКРЫТО]");
        value = AttachedPasswordRegex().Replace(value, "$1[СКРЫТО]");
        return value;
    }

    [GeneratedRegex(@"(?i)(--?(?:password|passphrase|token|secret|api[-_]?key)(?:=|\s+))([^\s]+)")]
    private static partial Regex InlineSecretRegex();

    [GeneratedRegex(@"(?i)(\b(?:mysql|mariadb|mysqladmin|mysqldump)\s+[^\r\n]*?-p)([^\s]+)")]
    private static partial Regex AttachedPasswordRegex();
}
