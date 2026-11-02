namespace LazyLinuxSSH.Core.Models;

public sealed class CommandTemplate
{
    public static Guid DefaultId { get; } = Guid.Parse("4d6f3d1f-8c0b-4ef4-b3cb-8d8f935a8001");

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string SelectedCommandKeysJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
