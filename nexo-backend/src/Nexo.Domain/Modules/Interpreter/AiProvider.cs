namespace Nexo.Domain.Modules.Interpreter;

/// <summary>
/// Platform-global LLM provider configuration.
/// NOT a TenantEntity — shared across the whole platform, managed by super_admin.
/// API keys are stored AES-256 encrypted; only the last 4 chars are exposed.
/// </summary>
public class AiProvider
{
    public Guid   Id                        { get; private set; }
    public string Name                      { get; private set; } = string.Empty;
    public string Provider                  { get; private set; } = string.Empty; // RuleBased | Claude | OpenAI
    public bool   IsEnabled                 { get; private set; }
    public bool   IsDefault                 { get; private set; }
    public string? ApiKeyEncrypted          { get; private set; }
    public string? ApiKeyLastFour           { get; private set; }
    public string? ModelId                  { get; private set; }
    public long?  MonthlyTokenLimit         { get; private set; }
    public long   CostPerInputTokenMicros   { get; private set; }
    public long   CostPerOutputTokenMicros  { get; private set; }
    public Guid?  FallbackProviderId        { get; private set; }
    public int    Priority                  { get; private set; }
    public DateTime UpdatedAt               { get; private set; }

    private AiProvider() { }

    public static AiProvider Create(
        string name,
        string provider,
        bool   isEnabled,
        bool   isDefault,
        int    priority,
        string? modelId                   = null,
        long?  monthlyTokenLimit          = null,
        long   costPerInputTokenMicros    = 0,
        long   costPerOutputTokenMicros   = 0,
        Guid?  fallbackProviderId         = null)
    {
        return new AiProvider
        {
            Id                       = Guid.NewGuid(),
            Name                     = name,
            Provider                 = provider,
            IsEnabled                = isEnabled,
            IsDefault                = isDefault,
            Priority                 = priority,
            ModelId                  = modelId,
            MonthlyTokenLimit        = monthlyTokenLimit,
            CostPerInputTokenMicros  = costPerInputTokenMicros,
            CostPerOutputTokenMicros = costPerOutputTokenMicros,
            FallbackProviderId       = fallbackProviderId,
            UpdatedAt                = DateTime.UtcNow,
        };
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetApiKey(string encryptedKey, string lastFour)
    {
        ApiKeyEncrypted = encryptedKey;
        ApiKeyLastFour  = lastFour;
        UpdatedAt       = DateTime.UtcNow;
    }

    public void ClearApiKey()
    {
        ApiKeyEncrypted = null;
        ApiKeyLastFour  = null;
        UpdatedAt       = DateTime.UtcNow;
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateLimits(long? monthlyTokenLimit, long costPerInput, long costPerOutput)
    {
        MonthlyTokenLimit        = monthlyTokenLimit;
        CostPerInputTokenMicros  = costPerInput;
        CostPerOutputTokenMicros = costPerOutput;
        UpdatedAt                = DateTime.UtcNow;
    }
}
