using Microsoft.EntityFrameworkCore;
using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Infrastructure.Data;

public sealed class LazyLinuxDbContext(DbContextOptions<LazyLinuxDbContext> options) : DbContext(options)
{
    public DbSet<ConnectionGroup> Groups => Set<ConnectionGroup>();
    public DbSet<ConnectionProfile> Profiles => Set<ConnectionProfile>();
    public DbSet<CustomCommand> CustomCommands => Set<CustomCommand>();
    public DbSet<CommandHistoryEntry> CommandHistory => Set<CommandHistoryEntry>();
    public DbSet<CommandTemplate> CommandTemplates => Set<CommandTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConnectionGroup>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasMany(x => x.Connections)
                .WithOne(x => x.Group)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasData(new ConnectionGroup
            {
                Id = Guid.Parse("0d62e35f-8f6b-4fbf-b3aa-e3b28df1a52a"),
                Name = "Основная",
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
            });
        });

        modelBuilder.Entity<ConnectionProfile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Host).IsRequired().HasMaxLength(255);
            entity.Property(x => x.Username).IsRequired().HasMaxLength(100);
            entity.Property(x => x.PrivateKeyPath).HasMaxLength(2048);
            entity.Property(x => x.Port).IsRequired();
        });

        modelBuilder.Entity<CustomCommand>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.CommandsJson).IsRequired();
        });

        modelBuilder.Entity<CommandHistoryEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CommandText).IsRequired().HasMaxLength(8000);
            entity.Property(x => x.ServerName).HasMaxLength(120);
            entity.HasIndex(x => x.ExecutedAt);
        });

        modelBuilder.Entity<CommandTemplate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.SelectedCommandKeysJson).IsRequired();
        });
    }
}
