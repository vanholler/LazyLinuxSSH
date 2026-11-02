namespace LazyLinuxSSH.Core.Interfaces;

public interface ICredentialProtector
{
    ValueTask<string?> ProtectAsync(string? plainText, CancellationToken cancellationToken = default);
    ValueTask<string?> UnprotectAsync(string? protectedText, CancellationToken cancellationToken = default);
}
