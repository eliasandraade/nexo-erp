using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Security;

/// <summary>
/// AES-256-GCM with a master key kept outside the database (<c>Secrets:MasterKey</c>: base64 of 32 random bytes, a
/// deployment secret). Envelope: <c>s1.{keyId}.{base64(nonce ‖ tag ‖ ciphertext)}</c> — a fresh 12-byte nonce per
/// encryption, a 16-byte tag, and the purpose as associated data (an envelope copied to another row or use won't open).
/// Rotation: set the new key as <c>Secrets:MasterKey</c> and keep the old one in <c>Secrets:PreviousMasterKeys</c>
/// (comma-separated) until every envelope has been re-sealed; the key id says which key sealed each envelope.
/// No master key → <see cref="IsAvailable"/> is false: nothing is encrypted with a fallback key, nothing is decrypted.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const string Prefix = "s1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly (string Id, byte[] Key)? _current;
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    public SecretProtector(IConfiguration config, ILogger<SecretProtector> logger)
    {
        var current = Parse(config["Secrets:MasterKey"], "Secrets:MasterKey", logger);
        if (current is not null)
        {
            _current = (KeyIdOf(current), current);
            _keys[_current.Value.Id] = current;
        }
        foreach (var previous in (config["Secrets:PreviousMasterKeys"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Parse(previous, "Secrets:PreviousMasterKeys", logger) is { } key)
                _keys.TryAdd(KeyIdOf(key), key);
        if (_current is null)
            logger.LogWarning("Secrets:MasterKey is not configured: secrets (AI provider keys) can't be stored or read in this deployment.");
    }

    public bool IsAvailable => _current is not null;

    public string Protect(string plaintext, string purpose)
    {
        if (_current is not { } current) throw new SecretProtectionUnavailableException();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(current.Key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, Aad(purpose));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
        return $"{Prefix}{current.Id}.{Convert.ToBase64String([.. nonce, .. tag, .. cipher])}";
    }

    public string? Unprotect(string envelope, string purpose)
    {
        if (!TrySplit(envelope, out var keyId, out var payload) || !_keys.TryGetValue(keyId, out var key)) return null;
        try
        {
            var raw = Convert.FromBase64String(payload);
            if (raw.Length < NonceSize + TagSize) return null;
            var plain = new byte[raw.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(raw.AsSpan(0, NonceSize), raw.AsSpan(NonceSize + TagSize), raw.AsSpan(NonceSize, TagSize), plain, Aad(purpose));
            var text = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return text;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public bool IsEnvelope(string value) => TrySplit(value, out _, out _);

    public bool NeedsRotation(string envelope)
        => _current is { } current && TrySplit(envelope, out var keyId, out _) && keyId != current.Id && _keys.ContainsKey(keyId);

    private static bool TrySplit(string value, out string keyId, out string payload)
    {
        keyId = payload = "";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var dot = value.IndexOf('.', Prefix.Length);
        if (dot != Prefix.Length + 8) return false;
        keyId = value[Prefix.Length..dot];
        payload = value[(dot + 1)..];
        return payload.Length > 0;
    }

    private static byte[] Aad(string purpose) => Encoding.UTF8.GetBytes("orken/secret/v1|" + purpose);

    private static string KeyIdOf(byte[] key)
        => Convert.ToHexString(SHA256.HashData([.. Encoding.UTF8.GetBytes("orken/secret-key-id/v1|"), .. key]))[..8].ToLowerInvariant();

    private static byte[]? Parse(string? base64, string setting, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var key = Convert.FromBase64String(base64.Trim());
            if (key.Length == 32) return key;
        }
        catch (FormatException)
        {
            // reported below without the value
        }
        logger.LogError("{Setting} is not the base64 of 32 bytes: it is ignored.", setting);
        return null;
    }
}
