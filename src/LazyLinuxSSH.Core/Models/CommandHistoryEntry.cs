namespace LazyLinuxSSH.Core.Models;

public enum CommandHistorySource
{
    Interactive = 0,
    QuickCommand = 1,
    CustomCommand = 2
}

public sealed class CommandHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CommandText { get; set; } = string.Empty;
    public string? ServerName { get; set; }
    public CommandHistorySource Source { get; set; }
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
}
