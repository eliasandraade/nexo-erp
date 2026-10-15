using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// AES-256-GCM (associated data = the user id) with keys derived by HKDF from <c>Mfa:EncryptionKey</c>
/// when configured, else from <c>Jwt:Secret</c> — no new secret is required to run. Format
/// "v1:" + base64(nonce ‖ tag ‖ ciphertext). If the source secret changes, secrets become unreadable:
/// the code check fails cleanly (logged) and people sign in with a recovery code or a director
/// turns two-step verification off; they then enrol again.
/// </summary>
public sealed class MfaSecretProtector : IMfaSecretProtector
{
    private const string Prefix = "v1:";
    private readonly byte[] _encryptionKey;
    private readonly byte[] _recoveryKey;
    private readonly ILogger<MfaSecretProtector>? _logger;

    public MfaSecretProtector(IConfiguration config, ILogger<MfaSecretProtector>? logger = null)
    {
        _logger = logger;
        var source = config["Mfa:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(source))
            source = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        var ikm = Encoding.UTF8.GetBytes(source);
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: Encoding.UTF8.GetBytes("orken/mfa-secret/v1"));
        _recoveryKey   = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: Encoding.UTF8.GetBytes("orken/mfa-recovery/v1"));
    }

    public string Protect(byte[] secret, Guid userId)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[secret.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, secret, cipher, tag, userId.ToByteArray());
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public byte[]? Unprotect(string protectedSecret, Guid userId)
    {
        try
        {
            if (!protectedSecret.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var raw = Convert.FromBase64String(protectedSecret[Prefix.Length..]);
            if (raw.Length < 29) return null;
            var plain = new byte[raw.Length - 28];
            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(raw[..12], raw[28..], raw[12..28], plain, userId.ToByteArray());
            return plain;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger?.LogError("Two-step verification secret of user {UserId} can't be read (key changed or data tampered).", userId);
            return null;
        }
    }

    public string HashRecoveryCode(string normalizedCode)
        => Convert.ToHexString(HMACSHA256.HashData(_recoveryKey, Encoding.UTF8.GetBytes(normalizedCode))).ToLowerInvariant();
}
