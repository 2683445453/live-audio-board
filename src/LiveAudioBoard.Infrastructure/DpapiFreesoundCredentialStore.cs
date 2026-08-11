using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using LiveAudioBoard.Core.Downloads;

namespace LiveAudioBoard.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class DpapiFreesoundCredentialStore : IFreesoundCredentialStore
{
    private const int MaximumCredentialFileBytes = 64 * 1024;

    private static readonly byte[] AdditionalEntropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("LiveAudioBoard.Freesound.Credentials.v1"));

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public DpapiFreesoundCredentialStore(string credentialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);
        CredentialPath = Path.GetFullPath(credentialPath);
    }

    public string CredentialPath { get; }

    public static DpapiFreesoundCredentialStore CreateDefault()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveAudioBoard");
        return new DpapiFreesoundCredentialStore(
            Path.Combine(dataDirectory, "freesound.auth"));
    }

    public async Task<FreesoundCredentialSet?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(CredentialPath))
            {
                return null;
            }

            var fileInfo = new FileInfo(CredentialPath);
            if (fileInfo.Length is <= 0 or > MaximumCredentialFileBytes)
            {
                throw new InvalidDataException("Freesound 授权文件大小无效。");
            }

            var encrypted = await File.ReadAllBytesAsync(CredentialPath, cancellationToken);
            byte[]? plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(
                    encrypted,
                    AdditionalEntropy,
                    DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<FreesoundCredentialSet>(
                    plaintext,
                    SerializerOptions);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidDataException(
                    "Freesound 授权数据无法由当前 Windows 用户解密。",
                    exception);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Freesound 授权数据已损坏。", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        FreesoundCredentialSet credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(CredentialPath)
                ?? throw new InvalidOperationException("无法确定 Freesound 授权目录。");
            Directory.CreateDirectory(directory);

            var plaintext = JsonSerializer.SerializeToUtf8Bytes(
                credentials,
                SerializerOptions);
            byte[]? encrypted = null;
            try
            {
                encrypted = ProtectedData.Protect(
                    plaintext,
                    AdditionalEntropy,
                    DataProtectionScope.CurrentUser);
                var temporaryPath = CredentialPath + ".tmp";
                await File.WriteAllBytesAsync(
                    temporaryPath,
                    encrypted,
                    cancellationToken);
                File.Move(temporaryPath, CredentialPath, true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                if (encrypted is not null)
                {
                    CryptographicOperations.ZeroMemory(encrypted);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(CredentialPath))
            {
                File.Delete(CredentialPath);
            }

            var temporaryPath = CredentialPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
