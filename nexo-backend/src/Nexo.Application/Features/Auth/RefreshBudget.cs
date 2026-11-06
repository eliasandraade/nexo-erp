using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Application.Features.Auth;

/// <summary>The two Redis budgets of the renewal policy (<c>RateLimiting:AuthRefresh:*</c>).</summary>
public sealed class RefreshBudgetOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>Invalid tokens per address and window. A client of ours never sends one: this is trial and error.</summary>
    public int FailureBudget { get; init; } = 30;
    public TimeSpan FailureWindow { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Renewals per session and window. A session renews once per access token (15 min), so 60 is a client in a loop.</summary>
    public int FamilyBudget { get; init; } = 300;
    public TimeSpan FamilyWindow { get; init; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// What a store with many terminals behind one address needs from the renewal limit: counting what is wrong instead of counting
/// the address. The in-memory ceiling (<c>auth-refresh</c>) stops a flood; these two stop a guess and a loop, and neither can
/// touch a healthy session of another terminal.
///
/// Without Redis they stop counting and the renewal goes on as it would without them. Over budget, the answer is "wait", and
/// nothing about the session changes.
/// </summary>
public sealed class RefreshBudget
{
    private readonly ICacheService _cache;
    private readonly RefreshBudgetOptions _options;
    private readonly ILogger<RefreshBudget> _logger;

    public RefreshBudget(ICacheService cache, RefreshBudgetOptions options, ILogger<RefreshBudget> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public static string FailureKey(string clientIp) => $"refresh:budget:ip:{AddressPart(clientIp)}";

    public static string FamilyKey(string family) => $"refresh:budget:family:{family}";

    /// <summary>Counts an invalid token from this address. The time to wait when over budget; otherwise null.</summary>
    public Task<TimeSpan?> SpendFailureAsync(string clientIp, CancellationToken ct = default)
        => SpendAsync(FailureKey(clientIp), _options.FailureBudget, _options.FailureWindow, ct);

    /// <summary>Counts a renewal of this session. The time to wait when over budget; otherwise null.</summary>
    public Task<TimeSpan?> SpendFamilyAsync(string family, CancellationToken ct = default)
        => SpendAsync(FamilyKey(family), _options.FamilyBudget, _options.FamilyWindow, ct);

    private async Task<TimeSpan?> SpendAsync(string key, int budget, TimeSpan window, CancellationToken ct)
    {
        if (!_options.Enabled) return null;

        if (await _cache.CountInWindowAsync(key, window, ct) is not { } counted || counted.Count <= budget) return null;

        // once per window: the first call over budget
        if (counted.Count == budget + 1)
            _logger.LogWarning("Refresh budget spent for {Key} ({Budget} in {Seconds} s).", key, budget, window.TotalSeconds);
        return counted.Remaining;
    }

    /// <summary>
    /// The address comes from a request header, and it becomes a Redis key: anything that is not an address has a bounded size
    /// there, whatever was sent.
    /// </summary>
    private static string AddressPart(string clientIp)
        => IPAddress.TryParse(clientIp, out _)
            ? clientIp
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientIp)));
}
