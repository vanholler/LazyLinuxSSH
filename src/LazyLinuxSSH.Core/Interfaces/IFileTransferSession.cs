using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public interface IFileTransferSession
{
    /// <summary>
    /// Absolute, home-relative (~/...) or unset working directory of the interactive shell.
    /// Used to resolve relative editor paths such as <c>.env</c>.
    /// </summary>
    string? WorkingDirectory { get; set; }

    Task<TextFileContent> ReadTextFileAsync(string path, CancellationToken cancellationToken = default);
    Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a local file into the current working directory under <paramref name="remoteFileName"/>.
    /// </summary>
    Task UploadFileAsync(string localPath, string remoteFileName, CancellationToken cancellationToken = default);
}
