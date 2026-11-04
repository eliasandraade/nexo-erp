using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

/// <summary>
/// Platform-global LLM provider configuration, managed by super_admin — the single source of truth the runtime reads
/// (enabled, default, priority, model, key, limits, prices). NOT a TenantEntity.
/// The API key is kept only as an authenticated-encryption envelope (<see cref="ApiKeyEncrypted"/>, written by the
/// infrastructure secret protector); only its last four characters are ever shown.
/// </summary>
public class AiProvider
{
    public const int MaxModelIdLength = 100;

    public Guid   Id                        { get; private set; }
    public string Name                      { get; private set; } = string.Empty;
    public string Provider                  { get; private set; } = string.Empty; // RuleBased | Claude | OpenAI
    public bool   IsEnabled                 { get; private set; }
    public bool   IsDefault                 { get; private set; }
    public string? ApiKeyEncrypted          { get; private set; }
    public string? ApiKeyLastFour           { get; private set; }
    public DateTime? ApiKeyUpdatedAt        { get; private set; }
    public string? ModelId                  { get; private set; }
    public long?  MonthlyTokenLimit         { get; private set; }
    /// <summary>Legacy per-token prices in integer micro-dollars: too coarse for real prices; no longer read.</summary>
    public long   CostPerInputTokenMicros   { get; private set; }
    /// <summary>Legacy per-token prices in integer micro-dollars: too coarse for real prices; no longer read.</summary>
    public long   CostPerOutputTokenMicros  { get; private set; }
    /// <summary>USD micro-dollars per million input tokens (e.g. $0.10 / MTok = 100_000). Drives the cost in telemetry.</summary>
    public long   CostPerMillionInputMicros  { get; private set; }
    /// <summary>USD micro-dollars per million output tokens.</summary>
    public long   CostPerMillionOutputMicros { get; private set; }
    public Guid?  FallbackProviderId        { get; private set; }
    public int    Priority                  { get; private set; }
    /// <summary>Result of the last explicit connection test (sanitized; never the provider's raw body).</summary>
    public string?   LastTestStatus         { get; private set; }
    public string?   LastTestMessage        { get; private set; }
    public DateTime? LastTestAt             { get; private set; }
    public DateTime UpdatedAt               { get; private set; }

    private AiProvider() { }

    public static AiProvider Create(
        string name,
        string provider,
        bool   isEnabled,
        bool   isDefault,
        int    priority,
        string? modelId                    = null,
        long?  monthlyTokenLimit           = null,
        long   costPerMillionInputMicros   = 0,
        long   costPerMillionOutputMicros  = 0)
    {
        return new AiProvider
        {
            Id                         = Guid.NewGuid(),
            Name                       = name,
            Provider                   = provider,
            IsEnabled                  = isEnabled,
            IsDefault                  = isDefault,
            Priority                   = priority,
            ModelId                    = modelId,
            MonthlyTokenLimit          = monthlyTokenLimit,
            CostPerMillionInputMicros  = costPerMillionInputMicros,
            CostPerMillionOutputMicros = costPerMillionOutputMicros,
            UpdatedAt                  = DateTime.UtcNow,
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        Touch();
    }

    /// <summary>Stores the encrypted envelope of a new key. The plaintext never reaches the entity.</summary>
    public void SetApiKey(string encryptedKey, string lastFour)
    {
        if (string.IsNullOrWhiteSpace(encryptedKey)) throw new DomainException("Chave inválida.", "ai_key_invalid");
        ApiKeyEncrypted = encryptedKey;
        ApiKeyLastFour  = lastFour;
        ApiKeyUpdatedAt = DateTime.UtcNow;
        ResetTest();
        Touch();
    }

    /// <summary>Re-encrypts the stored key in a new envelope (legacy upgrade or master-key rotation) — same key, same last four.</summary>
    public void ReplaceKeyEnvelope(string encryptedKey)
    {
        if (string.IsNullOrWhiteSpace(encryptedKey)) throw new DomainException("Chave inválida.", "ai_key_invalid");
        ApiKeyEncrypted = encryptedKey;
        Touch();
    }

    public void ClearApiKey()
    {
        ApiKeyEncrypted = null;
        ApiKeyLastFour  = null;
        ApiKeyUpdatedAt = DateTime.UtcNow;
        ResetTest();
        Touch();
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        Touch();
    }

    /// <summary>The model id sent to the provider. Null clears it (the provider then reports "sem modelo").</summary>
    public void SetModel(string? modelId)
    {
        var value = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
        if (value is not null && (value.Length > MaxModelIdLength || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or ':' or '/')))
            throw new DomainException("Modelo inválido: use o identificador exato do provedor (letras, números, '-', '.', '_', ':' ou '/').", "ai_model_invalid");
        if (value != ModelId) ResetTest();
        ModelId = value;
        Touch();
    }

    public void SetPriority(int priority)
    {
        if (priority is < 1 or > 100) throw new DomainException("A prioridade vai de 1 a 100.", "ai_priority_invalid");
        Priority = priority;
        Touch();
    }

    public void SetMonthlyTokenLimit(long? monthlyTokenLimit)
    {
        if (monthlyTokenLimit is <= 0) throw new DomainException("O limite mensal de tokens precisa ser positivo (ou vazio para sem limite).", "ai_limit_invalid");
        MonthlyTokenLimit = monthlyTokenLimit;
        Touch();
    }

    public void SetPrices(long perMillionInputMicros, long perMillionOutputMicros)
    {
        if (perMillionInputMicros < 0 || perMillionOutputMicros < 0) throw new DomainException("Preço não pode ser negativo.", "ai_price_invalid");
        CostPerMillionInputMicros  = perMillionInputMicros;
        CostPerMillionOutputMicros = perMillionOutputMicros;
        Touch();
    }

    public void RecordTest(string status, string message, DateTime at)
    {
        LastTestStatus  = status;
        LastTestMessage = message.Length > 300 ? message[..300] : message;
        LastTestAt      = at;
        Touch();
    }

    private void ResetTest()
    {
        LastTestStatus  = null;
        LastTestMessage = null;
        LastTestAt      = null;
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
