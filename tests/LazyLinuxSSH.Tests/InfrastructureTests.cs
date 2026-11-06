using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Infrastructure.Commands;
using LazyLinuxSSH.Infrastructure.Data;
using LazyLinuxSSH.Infrastructure.Security;

namespace LazyLinuxSSH.Tests;

public sealed class CredentialProtectorTests
{
    [Fact]
    public async Task ProtectAndUnprotect_ReturnsOriginalSecret()
    {
        var previous = Environment.GetEnvironmentVariable("LAZYLINUXSSH_MASTER_KEY");
        Environment.SetEnvironmentVariable("LAZYLINUXSSH_MASTER_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        try
        {
            await using var protector = new CredentialProtector();
            var encrypted = await protector.ProtectAsync("p@ssword✓");
            Assert.NotNull(encrypted);
            Assert.DoesNotContain("p@ssword", encrypted);
            Assert.Equal("p@ssword✓", await protector.UnprotectAsync(encrypted));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAZYLINUXSSH_MASTER_KEY", previous);
        }
    }

    [Fact]
    public async Task Unprotect_TamperedPayload_ThrowsCryptographicException()
    {
        if (OperatingSystem.IsWindows()) return;
        var previous = Environment.GetEnvironmentVariable("LAZYLINUXSSH_MASTER_KEY");
        Environment.SetEnvironmentVariable("LAZYLINUXSSH_MASTER_KEY", "unit-test-key");
        try
        {
            await using var protector = new CredentialProtector();
            var encrypted = await protector.ProtectAsync("secret");
            var payload = Convert.FromBase64String(encrypted!["aesgcm:v1:".Length..]);
            payload[^1] ^= 0xFF;
            var tampered = "aesgcm:v1:" + Convert.ToBase64String(payload);
            await Assert.ThrowsAnyAsync<CryptographicException>(async () => await protector.UnprotectAsync(tampered));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAZYLINUXSSH_MASTER_KEY", previous);
        }
    }
}

public sealed class RepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lazylinuxssh-tests", Guid.NewGuid().ToString("N"));
    private readonly TestContextFactory _factory;

    public RepositoryTests()
    {
        Directory.CreateDirectory(_directory);
        var options = new DbContextOptionsBuilder<LazyLinuxDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "test.db")}")
            .Options;
        _factory = new TestContextFactory(options);
        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task ProfileAndGroup_CanBeSavedListedAndDeleted()
    {
        var repository = new ConnectionRepository(_factory);
        var group = await repository.AddGroupAsync("Production");
        var profile = new ConnectionProfile
        {
            GroupId = group.Id,
            Name = "API",
            Host = "10.0.0.2",
            Username = "deploy",
            EncryptedPassword = "encrypted"
        };
        await repository.SaveProfileAsync(profile);

        var groups = await repository.GetGroupsAsync();
        var saved = Assert.Single(groups.Single(item => item.Id == group.Id).Connections);
        Assert.Equal("10.0.0.2", saved.Host);

        saved.Name = "API-2";
        await repository.SaveProfileAsync(saved);
        Assert.Equal("API-2", (await repository.GetProfileAsync(profile.Id))!.Name);

        await repository.DeleteProfileAsync(profile.Id);
        Assert.Null(await repository.GetProfileAsync(profile.Id));
    }

    [Fact]
    public async Task AddGroup_DuplicateName_ThrowsReadableError()
    {
        var repository = new ConnectionRepository(_factory);
        await repository.AddGroupAsync("Staging");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddGroupAsync("staging"));
    }

    [Fact]
    public async Task QuickCommands_PersistSequenceAsJson()
    {
        var service = new QuickCommandService(_factory);
        var saved = await service.SaveCustomCommandAsync(null, "Check", ["pwd", "ls -la"]);
        var commands = await service.GetCustomCommandsAsync();
        var entity = Assert.Single(commands);
        Assert.Equal(saved.Id, entity.Id);
        Assert.Equal(["pwd", "ls -la"], QuickCommandService.ReadSequence(entity));
        Assert.Equal(29, service.GetBuiltInCategories().Count);
        Assert.Equal(483, service.GetBuiltInCategories().Sum(category => category.Commands.Count));
        Assert.All(service.GetBuiltInCategories(), category => Assert.False(string.IsNullOrWhiteSpace(category.Icon)));

        await service.DeleteCustomCommandAsync(saved.Id);
        Assert.Empty(await service.GetCustomCommandsAsync());
    }

    [Fact]
    public async Task CommandTemplates_CustomSelectionsPersist_DefaultStaysImmutableAndFirst()
    {
        await new DatabaseInitializer(_factory).InitializeAsync();
        var service = new CommandTemplateService(_factory);
        var saved = await service.SaveCustomAsync(null, "Сеть", ["Сеть::Маршруты"]);

        var templates = await service.GetAllAsync();
        Assert.Equal(2, templates.Count);
        Assert.Equal("Default", templates[0].Name);
        Assert.True(templates[0].IsDefault);
        Assert.Null(CommandTemplateService.ReadSelectedKeys(templates[0]));
        Assert.Equal(saved.Id, templates[1].Id);
        Assert.Equal(["Сеть::Маршруты"], CommandTemplateService.ReadSelectedKeys(templates[1])!);

        await service.SaveCustomAsync(saved.Id, "Сеть и файлы", ["Сеть::Маршруты", "Файлы::Список"]);
        Assert.Equal(2, (await service.GetAllAsync()).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCustomAsync(null, "default", ["Сеть::Маршруты"]));
    }

    [Fact]
    public async Task DatabaseInitializer_AddsHistoryToExistingDatabase()
    {
        var path = Path.Combine(_directory, "legacy.db");
        var options = new DbContextOptionsBuilder<LazyLinuxDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var factory = new TestContextFactory(options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"LegacyMarker\" (\"Id\" INTEGER PRIMARY KEY);");
        }

        await new DatabaseInitializer(factory).InitializeAsync();
        var history = new CommandHistoryService(factory);
        await history.AddAsync("pwd", "Legacy server", CommandHistorySource.Interactive);

        Assert.Equal("pwd", Assert.Single(await history.GetAllAsync()).CommandText);
        await using var verification = factory.CreateDbContext();
        Assert.Equal(1, await verification.Database.ExecuteSqlRawAsync("INSERT INTO \"LegacyMarker\" (\"Id\") VALUES (1);"));
    }

    [Fact]
    public async Task CommandHistory_PersistsSanitizesAndClears()
    {
        var initializer = new DatabaseInitializer(_factory);
        await initializer.InitializeAsync();
        var service = new CommandHistoryService(_factory);

        await service.AddAsync("mysql -pSuperSecret -u root", "Production API", CommandHistorySource.Interactive);
        await service.AddAsync("curl --token=abc123 https://example.com", "Production API", CommandHistorySource.QuickCommand);
        await service.AddAsync("ls -la", "Production API", CommandHistorySource.Interactive);

        var entries = await service.GetAllAsync();
        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.DoesNotContain("SuperSecret", entry.CommandText));
        Assert.All(entries, entry => Assert.DoesNotContain("abc123", entry.CommandText));
        Assert.Contains(entries, entry => entry.CommandText == "ls -la");

        await service.ClearAsync();
        Assert.Empty(await service.GetAllAsync());
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private sealed class TestContextFactory(DbContextOptions<LazyLinuxDbContext> options) : IDbContextFactory<LazyLinuxDbContext>
    {
        public LazyLinuxDbContext CreateDbContext() => new(options);
        public Task<LazyLinuxDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LazyLinuxDbContext(options));
    }
}

public sealed class CommandSequenceExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_SendsCommandsIndividuallyWithNewLine()
    {
        var session = new RecordingSession();
        var executor = new CommandSequenceExecutor();
        await executor.ExecuteAsync(session, ["pwd", "", "ls -la"], TimeSpan.Zero);
        Assert.Equal(["pwd\n", "ls -la\n"], session.Sent);
    }

    private sealed class RecordingSession : ISshSession
    {
        public List<string> Sent { get; } = [];
        public Guid Id { get; } = Guid.NewGuid();
        public ConnectionProfile Profile { get; } = new();
        public SshSessionState State => SshSessionState.Connected;
        public DateTimeOffset? LastOutputAt => null;
        public string? WorkingDirectory { get; set; }
        public event EventHandler<string>? OutputReceived { add { } remove { } }
        public event EventHandler<SshSessionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            Sent.Add(text);
            return Task.CompletedTask;
        }
        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<TextFileContent> ReadTextFileAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextFileContent(path, string.Empty, false));
        public Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UploadFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void MarkStale() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
