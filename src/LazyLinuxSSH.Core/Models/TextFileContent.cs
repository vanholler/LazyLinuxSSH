namespace LazyLinuxSSH.Core.Models;

public sealed record TextFileContent(string Path, string Text, bool Exists);
