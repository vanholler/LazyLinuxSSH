using LazyLinuxSSH.Core.Models;

namespace LazyLinuxSSH.Core.Interfaces;

public interface IFileTransferSession
{
    Task<TextFileContent> ReadTextFileAsync(string path, CancellationToken cancellationToken = default);
    Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default);
}
