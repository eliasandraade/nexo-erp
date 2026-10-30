using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Reads the SuperAdmin's provider configuration (<c>ai_providers</c>) and combines it with what this deployment
/// implements (registered analyzers) and allows (emergency switch <c>Ai:Providers:{Provider}:Allowed</c>, default true).
/// A provider runs only when it is enabled in the database AND allowed by the deployment — the deployment can only take
/// a provider away, never turn one on. Keys are decrypted in memory only when a call needs them.
/// </summary>
public sealed class AiProviderRegistry : IAiProviderRegistry
{
    /// <summary>A failed connection test keeps a provider out of automatic use for this long, unless the key or model changes.</summary>
    public static readonly TimeSpan TestFailureWindow = TimeSpan.FromMinutes(15);

    private const string ExtractionPrompt = "extraction";

    private readonly NexoDbContext _db;
    private readonly IEnumerable<IDocumentAnalyzer> _analyzers;
    private readonly ISecretProtector _secrets;
    private readonly IActivePromptProvider _prompts;
    private readonly IConfiguration _config;
    private readonly ILogger<AiProviderRegistry> _logger;

    public AiProviderRegistry(NexoDbContext db, IEnumerable<IDocumentAnalyzer> analyzers, ISecretProtector secrets,
        IActivePromptProvider prompts, IConfiguration config, ILogger<AiProviderRegistry> logger)
    {
        _db = db;
        _analyzers = analyzers;
        _secrets = secrets;
        _prompts = prompts;
        _config = config;
        _logger = logger;
    }

    public static string KeyPurpose(Guid providerId) => $"ai-provider/{providerId:N}/api-key";

    public static bool RequiresKey(AnalyzerProvider provider) => provider != AnalyzerProvider.RuleBased;

    public async Task<IReadOnlyList<AiProviderState>> GetStatesAsync(CancellationToken ct = default)
    {
        var rows = await _db.AiProviders.AsNoTracking().ToListAsync(ct);
        return rows
            .Select(r => (Row: r, Known: Enum.TryParse<AnalyzerProvider>(r.Provider, out var p) && Enum.IsDefined(p), Provider: p))
            .Where(x => x.Known)
            .Select(x => StateOf(x.Row, x.Provider, DateTime.UtcNow))
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.Priority).ThenBy(s => s.Provider)
            .ToList();
    }

    public async Task<AnalyzerConfig?> ResolveAsync(AnalyzerProvider provider, CancellationToken ct = default)
    {
        var name = provider.ToString();
        var row = await _db.AiProviders.AsNoTracking().FirstOrDefaultAsync(r => r.Provider == name, ct);
        if (row is null)
            throw new AiProviderUnavailableException($"O provedor {provider} não está cadastrado na plataforma.", provider, AiProviderStatus.Disabled);
        var state = StateOf(row, provider, DateTime.UtcNow);
        if (!state.IsAvailable)
            throw new AiProviderUnavailableException(state.StatusMessage, provider, state.Status);
        if (!RequiresKey(provider)) return null;

        var key = _secrets.Unprotect(row.ApiKeyEncrypted!, KeyPurpose(row.Id))
            ?? throw new AiProviderUnavailableException(MessageOf(AiProviderStatus.SecretUnavailable, provider), provider, AiProviderStatus.SecretUnavailable);
        var prompt = await _prompts.GetAsync(ExtractionPrompt, ct);
        return new AnalyzerConfig(provider, row.ModelId!, key, prompt, row.CostPerMillionInputMicros, row.CostPerMillionOutputMicros);
    }

    private AiProviderState StateOf(AiProvider row, AnalyzerProvider provider, DateTime now)
    {
        var implemented = _analyzers.Any(a => a.Provider == provider);
        var allowed = _config.GetValue($"Ai:Providers:{provider}:Allowed", true);
        var requiresKey = RequiresKey(provider);
        var status = StatusOf(row, implemented, allowed, requiresKey, now);
        return new AiProviderState(row.Id, provider, row.Name, status, MessageOf(status, provider), implemented, requiresKey, allowed,
            row.IsEnabled, row.IsDefault, row.Priority, row.ModelId, row.ApiKeyEncrypted is not null, row.ApiKeyLastFour, row.ApiKeyUpdatedAt,
            row.MonthlyTokenLimit, row.CostPerMillionInputMicros, row.CostPerMillionOutputMicros,
            row.LastTestStatus, row.LastTestMessage, row.LastTestAt);
    }

    private AiProviderStatus StatusOf(AiProvider row, bool implemented, bool allowed, bool requiresKey, DateTime now)
    {
        if (!implemented) return AiProviderStatus.ImplementationMissing;
        if (!allowed) return AiProviderStatus.BlockedByDeployment;
        if (!row.IsEnabled) return AiProviderStatus.Disabled;
        if (!requiresKey) return AiProviderStatus.Available;
        if (row.ApiKeyEncrypted is null) return AiProviderStatus.MissingKey;
        if (string.IsNullOrWhiteSpace(row.ModelId)) return AiProviderStatus.MissingModel;
        if (!_secrets.IsAvailable || !_secrets.IsEnvelope(row.ApiKeyEncrypted)) return AiProviderStatus.SecretUnavailable;
        if (row.LastTestAt is { } at && now - at < TestFailureWindow)
        {
            var failed = row.LastTestStatus switch
            {
                "invalid_credentials" => AiProviderStatus.InvalidCredentials,
                "rate_limited" => AiProviderStatus.RateLimited,
                "ok" or null => (AiProviderStatus?)null,
                _ => AiProviderStatus.Unhealthy,
            };
            if (failed is { } f) return f;
        }
        // A credential rejection stays until the key changes or a new test passes.
        if (row.LastTestStatus == "invalid_credentials") return AiProviderStatus.InvalidCredentials;
        return AiProviderStatus.Available;
    }

    public static string MessageOf(AiProviderStatus status, AnalyzerProvider provider) => status switch
    {
        AiProviderStatus.Available => "Configurado e disponível.",
        AiProviderStatus.Disabled => $"O provedor {provider} está desativado no SuperAdmin.",
        AiProviderStatus.BlockedByDeployment => $"O provedor {provider} está bloqueado nesta implantação (Ai:Providers:{provider}:Allowed=false).",
        AiProviderStatus.MissingKey => $"O provedor {provider} não tem chave de API cadastrada.",
        AiProviderStatus.MissingModel => $"O provedor {provider} não tem modelo definido.",
        AiProviderStatus.ImplementationMissing => $"O provedor {provider} não está disponível nesta versão do Orken.",
        AiProviderStatus.SecretUnavailable => $"A chave do provedor {provider} não pode ser lida nesta implantação (Secrets:MasterKey ausente ou diferente da que a cifrou). Cadastre a chave de novo.",
        AiProviderStatus.InvalidCredentials => $"A chave do provedor {provider} foi recusada no último teste. Cadastre uma chave válida e teste de novo.",
        AiProviderStatus.RateLimited => $"O provedor {provider} limitou as chamadas no último teste. Aguarde e teste de novo.",
        _ => $"O último teste do provedor {provider} falhou. Teste de novo antes de usar.",
    };

    /// <summary>
    /// Re-seals every stored key that is not in the current envelope: legacy Base64 values from before encryption and
    /// envelopes of a previous master key. Runs in memory only; nothing about the key is logged. Returns how many changed.
    /// </summary>
    public static async Task<int> UpgradeStoredKeysAsync(NexoDbContext db, ISecretProtector secrets, ILogger logger, CancellationToken ct)
    {
        if (!secrets.IsAvailable) return 0;
        var rows = await db.AiProviders.Where(r => r.ApiKeyEncrypted != null).ToListAsync(ct);
        var changed = 0;
        foreach (var row in rows)
        {
            var purpose = KeyPurpose(row.Id);
            string? plain;
            if (secrets.IsEnvelope(row.ApiKeyEncrypted!))
            {
                if (!secrets.NeedsRotation(row.ApiKeyEncrypted!)) continue;
                plain = secrets.Unprotect(row.ApiKeyEncrypted!, purpose);
            }
            else plain = DecodeLegacy(row.ApiKeyEncrypted!);
            if (string.IsNullOrEmpty(plain))
            {
                logger.LogWarning("AI provider {Provider}: the stored key can't be re-sealed; it must be entered again.", row.Provider);
                continue;
            }
            row.ReplaceKeyEnvelope(secrets.Protect(plain, purpose));
            changed++;
            logger.LogInformation("AI provider {Provider}: stored key re-sealed with the current master key.", row.Provider);
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Before encryption existed, keys were stored as Base64 of their UTF-8 text.</summary>
    public static string? DecodeLegacy(string stored)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(stored));
            return text.Length >= 8 && !text.Any(char.IsWhiteSpace) && !text.Any(char.IsControl) ? text : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
