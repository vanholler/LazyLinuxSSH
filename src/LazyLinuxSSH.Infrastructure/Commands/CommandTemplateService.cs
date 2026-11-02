using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Infrastructure.Data;

namespace LazyLinuxSSH.Infrastructure.Commands;

public sealed class CommandTemplateService(IDbContextFactory<LazyLinuxDbContext> contextFactory) : ICommandTemplateService
{
    public async Task<IReadOnlyList<CommandTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var templates = await context.CommandTemplates.AsNoTracking()
            .Where(template => !template.IsDefault && template.Id != CommandTemplate.DefaultId)
            .OrderBy(template => template.Name)
            .ToListAsync(cancellationToken);
        templates.Insert(0, CreateDefault());
        return templates;
    }

    public async Task<CommandTemplate> SaveCustomAsync(
        Guid? id,
        string name,
        IReadOnlyCollection<string> selectedCommandKeys,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 1 or > 120)
            throw new ArgumentException("Введите название шаблона (до 120 символов).", nameof(name));
        if (normalizedName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Имя Default зарезервировано для встроенного шаблона.");

        var keys = selectedCommandKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0) throw new ArgumentException("Выберите хотя бы одну команду.", nameof(selectedCommandKeys));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var duplicate = await context.CommandTemplates.AnyAsync(
            template => template.Name.ToLower() == normalizedName.ToLower() && (!id.HasValue || template.Id != id.Value),
            cancellationToken);
        if (duplicate) throw new InvalidOperationException("Шаблон с таким названием уже существует.");

        CommandTemplate? entity = null;
        if (id.HasValue && id.Value != CommandTemplate.DefaultId)
            entity = await context.CommandTemplates.SingleOrDefaultAsync(
                template => template.Id == id.Value && !template.IsDefault,
                cancellationToken);
        entity ??= new CommandTemplate { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        entity.Name = normalizedName;
        entity.IsDefault = false;
        entity.SelectedCommandKeysJson = JsonSerializer.Serialize(keys);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        if (context.Entry(entity).State == EntityState.Detached) await context.CommandTemplates.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public static IReadOnlySet<string>? ReadSelectedKeys(CommandTemplate template)
    {
        if (template.IsDefault || template.Id == CommandTemplate.DefaultId) return null;
        try
        {
            var keys = JsonSerializer.Deserialize<string[]>(template.SelectedCommandKeysJson) ?? [];
            return new HashSet<string>(keys, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static CommandTemplate CreateDefault() => new()
    {
        Id = CommandTemplate.DefaultId,
        Name = "Default",
        IsDefault = true,
        SelectedCommandKeysJson = "[]",
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
    };
}
