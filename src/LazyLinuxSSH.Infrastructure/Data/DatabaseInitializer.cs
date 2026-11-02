using Microsoft.EntityFrameworkCore;
using LazyLinuxSSH.Core.Interfaces;

namespace LazyLinuxSSH.Infrastructure.Data;

public sealed class DatabaseInitializer(IDbContextFactory<LazyLinuxDbContext> contextFactory) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CommandHistory" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CommandHistory" PRIMARY KEY,
                "CommandText" TEXT NOT NULL,
                "ServerName" TEXT NULL,
                "Source" INTEGER NOT NULL,
                "ExecutedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_CommandHistory_ExecutedAt"
                ON "CommandHistory" ("ExecutedAt");
            CREATE TABLE IF NOT EXISTS "CommandTemplates" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CommandTemplates" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "IsDefault" INTEGER NOT NULL,
                "SelectedCommandKeysJson" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommandTemplates_Name"
                ON "CommandTemplates" ("Name");
            """, cancellationToken);
    }
}
