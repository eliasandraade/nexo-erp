namespace Nexo.Application.Integrations.Contracts;

/// <summary>
/// A private file (<see cref="StorageKeys.PrivatePrefix"/>) was about to be written, read, signed or deleted but no
/// private bucket is configured. Private content never falls back to the public bucket: the operation fails closed and
/// the caller answers 503 (storage unavailable).
/// </summary>
public sealed class PrivateStorageUnavailableException : InvalidOperationException
{
    public PrivateStorageUnavailableException()
        : base("Armazenamento privado não configurado: arquivos privados ficam indisponíveis até lá.") { }
}
