using System.Security.Cryptography;
using System.Text;
using LazyLinuxSSH.Core.Interfaces;
using LazyLinuxSSH.Infrastructure.Data;

namespace LazyLinuxSSH.Infrastructure.Security;

public sealed class CredentialProtector : ICredentialProtector, IAsyncDisposable
{
    private const string EnvironmentKeyName = "LAZYLINUXSSH_MASTER_KEY";
    private const string AesPrefix = "aesgcm:v1:";
    private const string DpapiPrefix = "dpapi:v1:";
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private byte[]? _aesKey;

    public async ValueTask<string?> ProtectAsync(string? plainText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(plainText)) return null;
        var bytes = Encoding.UTF8.GetBytes(plainText);
        if (OperatingSystem.IsWindows())
        {
            return DpapiPrefix + Convert.ToBase64String(
                ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }

        var key = await GetAesKeyAsync(cancellationToken);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[bytes.Length];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, bytes, cipher, tag);
        }

        CryptographicOperations.ZeroMemory(bytes);
        return AesPrefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public async ValueTask<string?> UnprotectAsync(string? protectedText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        if (protectedText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("DPAPI-запись можно расшифровать только в Windows текущим пользователем.");
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedText[DpapiPrefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        if (!protectedText.StartsWith(AesPrefix, StringComparison.Ordinal))
            throw new CryptographicException("Неизвестный формат зашифрованных данных.");

        var payload = Convert.FromBase64String(protectedText[AesPrefix.Length..]);
        if (payload.Length < 29) throw new CryptographicException("Повреждённые зашифрованные данные.");
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plain = new byte[cipher.Length];
        var key = await GetAesKeyAsync(cancellationToken);
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private async ValueTask<byte[]> GetAesKeyAsync(CancellationToken cancellationToken)
    {
        if (_aesKey is not null) return _aesKey;
        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (_aesKey is not null) return _aesKey;
            var configured = Environment.GetEnvironmentVariable(EnvironmentKeyName);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    var decoded = Convert.FromBase64String(configured);
                    _aesKey = decoded.Length == 32 ? decoded : SHA256.HashData(Encoding.UTF8.GetBytes(configured));
                }
                catch (FormatException)
                {
                    _aesKey = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
                }

                return _aesKey;
            }

            // Portable installations work out of the box. Environment key is preferable
            // for shared/headless setups; the fallback key is user-only on Unix.
            if (File.Exists(AppPaths.LocalKeyPath))
            {
                _aesKey = Convert.FromBase64String(await File.ReadAllTextAsync(AppPaths.LocalKeyPath, cancellationToken));
                if (_aesKey.Length != 32) throw new CryptographicException("Некорректный локальный ключ хранилища.");
            }
            else
            {
                _aesKey = RandomNumberGenerator.GetBytes(32);
                await File.WriteAllTextAsync(AppPaths.LocalKeyPath, Convert.ToBase64String(_aesKey), cancellationToken);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(AppPaths.LocalKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            return _aesKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_aesKey is not null) CryptographicOperations.ZeroMemory(_aesKey);
        _keyLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
