namespace LazyLinuxSSH.Core.Models;

public sealed record QuickCommand(string Name, string Command, string Description, bool IsDangerous = false);

public sealed record QuickCommandCategory(string Name, string Icon, IReadOnlyList<QuickCommand> Commands);
