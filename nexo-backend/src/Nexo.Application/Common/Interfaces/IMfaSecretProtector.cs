namespace Nexo.Application.Common.Interfaces;

/// <summary>Encrypts the TOTP secrets at rest and keys the recovery-code hashes (a database dump alone is useless).</summary>
public interface IMfaSecretProtector
{
    /// <summary>Encrypts, bound to the user (a ciphertext can't be moved to another account).</summary>
    string Protect(byte[] secret, Guid userId);
    /// <summary>Null when it can't be read (key rotated, tampered) — never throws.</summary>
    byte[]? Unprotect(string protectedSecret, Guid userId);
    /// <summary>HMAC of a normalised recovery code (not a plain hash: offline guessing needs the key).</summary>
    string HashRecoveryCode(string normalizedCode);
}
