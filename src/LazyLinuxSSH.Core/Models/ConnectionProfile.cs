namespace LazyLinuxSSH.Core.Models;

public enum AuthenticationMethod
{
    Password,
    PrivateKey
}

public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public AuthenticationMethod AuthenticationMethod { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? EncryptedPassphrase { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ConnectionGroup? Group { get; set; }
}
