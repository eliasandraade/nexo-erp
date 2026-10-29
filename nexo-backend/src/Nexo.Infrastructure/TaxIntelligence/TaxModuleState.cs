using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.TaxIntelligence.Files;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// The module's runtime state, read once per request: the newest platform record (activation, suspension or
/// deactivation) and whether the installation configuration is valid. Shared by the gate and the admin screens.
/// </summary>
public sealed class TaxModuleState
{
    private readonly NexoDbContext _db;
    private readonly TaxIntelligencePolicyOptions _policy;
    private readonly TaxFileOptions _files;
    private TaxModuleActivation? _latest;
    private bool _loaded;

    public TaxModuleState(NexoDbContext db, IOptions<TaxIntelligencePolicyOptions> policy, IOptions<TaxFileOptions> files)
    {
        _db = db;
        _policy = policy.Value;
        _files = files.Value;
    }

    public TaxIntelligencePolicyOptions Policy => _policy;

    /// <summary>The newest state record, or null when the platform never activated the module.</summary>
    public async Task<TaxModuleActivation?> LatestAsync(CancellationToken ct)
    {
        if (_loaded) return _latest;
        _latest = await _db.TaxModuleActivations.AsNoTracking().OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        _loaded = true;
        return _latest;
    }

    /// <summary>Forget the cached record (after this request wrote a new one).</summary>
    public void Invalidate() => _loaded = false;

    /// <summary>Problems in the installation configuration. Any problem keeps the module fail-closed.</summary>
    public IReadOnlyList<string> ConfigurationProblems => TaxConfigurationCheck.Problems(_policy, _files);
}

/// <summary>
/// Validates the installation configuration of Tax Intelligence. An invalid configuration never takes the ERP down:
/// the module stays fail-closed (no new work) and the problem is logged at startup and shown in the readiness view.
/// </summary>
public static class TaxConfigurationCheck
{
    public static IReadOnlyList<string> Problems(TaxIntelligencePolicyOptions policy, TaxFileOptions files)
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(policy.Mode)) problems.Add($"TaxIntelligence:Policy:Mode inválido ({(int)policy.Mode}).");
        foreach (var (name, value) in new[] { ("LowRisk", policy.LowRisk), ("MediumRisk", policy.MediumRisk), ("HighRisk", policy.HighRisk) })
            if (!Enum.IsDefined(value)) problems.Add($"TaxIntelligence:Policy:{name} inválido ({(int)value}).");
        if (policy.HighRisk < policy.MediumRisk || policy.MediumRisk < policy.LowRisk)
            problems.Add("Maker-checker: um risco maior não pode exigir menos que um risco menor (LowRisk ≤ MediumRisk ≤ HighRisk).");
        if (policy.MaterialityThreshold is <= 0)
            problems.Add("TaxIntelligence:Policy:MaterialityThreshold precisa ser positivo (ou ausente).");
        if (files.MaxFileSizeMb is < 1 or > 25)
            problems.Add("TaxIntelligence:Evidence:MaxFileSizeMb vai de 1 a 25.");
        return problems;
    }
}

/// <summary>
/// Logs, once at startup, whether the Tax Intelligence configuration is valid. It never stops the application: an invalid configuration
/// keeps only this module fail-closed (see <see cref="TaxConfigurationCheck"/>) and is shown in the platform readiness view.
/// </summary>
public static class TaxConfigurationStartupLog
{
    public static void Write(IServiceProvider services)
    {
        var p = services.GetRequiredService<IOptions<TaxIntelligencePolicyOptions>>().Value;
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Nexo.TaxIntelligence");
        var problems = TaxConfigurationCheck.Problems(p, services.GetRequiredService<IOptions<TaxFileOptions>>().Value);
        if (problems.Count > 0)
            log.LogError("Tax Intelligence configuration is invalid; the module stays fail-closed: {Problems}", string.Join(" ", problems));
        else
            log.LogInformation("Tax Intelligence configuration: Enabled={Enabled}, Mode={Mode} (new work also needs the platform activation of that mode).",
                p.Enabled, p.Mode);
    }
}
