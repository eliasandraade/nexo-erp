using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Nexo.Api.Middleware;

/// <summary>
/// The ceiling of the renewal policy (<c>RateLimiting:AuthRefresh:{Enabled,PermitLimit,WindowSeconds}</c>), per client address and
/// shared by <c>auth/refresh</c> and <c>platform/auth/refresh</c>. It only stops a flood, so it has room for a large store: 100
/// terminals renewing every 15 minutes, with reloads and tabs on top. In memory on purpose: it holds when Redis is out, when
/// the two budgets that do the finer work stop counting (<see cref="Nexo.Application.Features.Auth.RefreshBudget"/>).
/// </summary>
public static class AuthRefreshRateLimit
{
    public const string Policy = "auth-refresh";
    public const int DefaultPermitLimit = 900;
    public const int DefaultWindowSeconds = 900;

    public static void AddPolicy(RateLimiterOptions options, IConfiguration config, ClientIpResolver clientIp)
    {
        var enabled = config.GetValue("RateLimiting:AuthRefresh:Enabled", true);
        var permit = Math.Max(1, config.GetValue("RateLimiting:AuthRefresh:PermitLimit", DefaultPermitLimit));
        var window = Math.Max(1, config.GetValue("RateLimiting:AuthRefresh:WindowSeconds", DefaultWindowSeconds));

        options.AddPolicy(Policy, httpContext =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter($"{Policy}:disabled");

            return RateLimitPartition.GetFixedWindowLimiter($"{Policy}|{clientIp.Resolve(httpContext)}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = permit,
                    Window               = TimeSpan.FromSeconds(window),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                });
        });
    }
}
