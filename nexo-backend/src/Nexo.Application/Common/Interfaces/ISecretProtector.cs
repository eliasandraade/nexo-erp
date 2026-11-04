namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Authenticated encryption of secrets kept in the database (AI provider keys, …) with a master key that lives outside
/// the database (deployment secret). Each envelope is bound to a <c>purpose</c> (associated data), so an envelope copied
/// to another row or use does not decrypt. Plaintext is never logged nor returned to clients.
/// </summary>
public interface ISecretProtector
{
    /// <summary>False when no master key is configured: nothing can be encrypted or decrypted here.</summary>
    bool IsAvailable { get; }

    /// <summary>Encrypts with the current master key. Throws <see cref="SecretProtectionUnavailableException"/> when unavailable.</summary>
    string Protect(string plaintext, string purpose);

    /// <summary>
    /// The plaintext, or null when the envelope can't be opened here (unknown or missing master key, tampered data,
    /// wrong purpose). Never throws for bad input.
    /// </summary>
    string? Unprotect(string envelope, string purpose);

    /// <summary>True when the value is an envelope of this protector (any key version).</summary>
    bool IsEnvelope(string value);

    /// <summary>True when the envelope was sealed with a master key other than the current one (rotation pending).</summary>
    bool NeedsRotation(string envelope);
}

/// <summary>No master key is configured, so a secret can't be stored safely. Answered as 422 <c>secret_storage_unavailable</c>.</summary>
public sealed class SecretProtectionUnavailableException : Nexo.Domain.Exceptions.DomainException
{
    public SecretProtectionUnavailableException()
        : base("O armazenamento seguro de segredos não está configurado nesta implantação (Secrets:MasterKey). Nenhuma chave é gravada sem criptografia.",
            "secret_storage_unavailable") { }
}
