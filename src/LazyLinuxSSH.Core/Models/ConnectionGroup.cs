namespace LazyLinuxSSH.Core.Models;

public sealed class ConnectionGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ConnectionProfile> Connections { get; set; } = [];
}
