using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Infrastructure.Data;

namespace LazyLinuxSSH.Infrastructure.Commands;

public sealed class QuickCommandService(IDbContextFactory<LazyLinuxDbContext> contextFactory) : IQuickCommandService
{
    private const string CatalogResourceName = "LazyLinuxSSH.Infrastructure.Commands.linux-commands.json";
    private static readonly Lazy<IReadOnlyList<QuickCommandCategory>> BuiltIn = new(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<QuickCommandCategory> GetBuiltInCategories() => BuiltIn.Value;

    private static IReadOnlyList<QuickCommandCategory> LoadCatalog()
    {
        var assembly = typeof(QuickCommandService).Assembly;
        using var stream = assembly.GetManifestResourceStream(CatalogResourceName)
            ?? throw new InvalidOperationException("Встроенный каталог Linux-команд не найден.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var categories = JsonSerializer.Deserialize<List<QuickCommandCategory>>(stream, options)
            ?? throw new InvalidOperationException("Встроенный каталог Linux-команд пуст.");
        if (categories.Count == 0 || categories.Any(category => category.Commands.Count == 0))
            throw new InvalidOperationException("Встроенный каталог Linux-команд повреждён.");
        return categories;
    }

    public async Task<IReadOnlyList<CustomCommand>> GetCustomCommandsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.CustomCommands.AsNoTracking().OrderBy(command => command.Name).ToListAsync(cancellationToken);
    }

    public async Task<CustomCommand> SaveCustomCommandAsync(
        Guid? id,
        string name,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        var normalizedCommands = commands.Select(command => command.Trim()).Where(command => command.Length > 0).ToArray();
        if (normalizedName.Length is < 1 or > 120)
            throw new ArgumentException("Введите название команды (до 120 символов).", nameof(name));
        if (normalizedCommands.Length == 0)
            throw new ArgumentException("Добавьте хотя бы одну команду.", nameof(commands));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = id.HasValue
            ? await context.CustomCommands.SingleOrDefaultAsync(command => command.Id == id.Value, cancellationToken)
            : null;
        entity ??= new CustomCommand();
        entity.Name = normalizedName;
        entity.CommandsJson = JsonSerializer.Serialize(normalizedCommands);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
        if (context.Entry(entity).State == EntityState.Detached) await context.CustomCommands.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task DeleteCustomCommandAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.CustomCommands.Where(command => command.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public static IReadOnlyList<string> ReadSequence(CustomCommand command)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(command.CommandsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
