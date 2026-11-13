using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Nexo.Api.Middleware;

/// <summary>
/// The sign-out answers with no session, on the proof of a refresh token, and an unknown token of the platform costs a read of the database: it has a
/// ceiling (<c>RateLimiting:AuthLogout:*</c>). Generous, because whoever is signing out must get through: a shift change in a store behind one address fits
/// well inside it, and someone with an access token counts apart from that address.
/// </summary>
public static class AuthLogoutRateLimit
{
    public const string Policy = "auth-logout";
    public const int DefaultPermitLimit = 300;
    public const int DefaultWindowSeconds = 900;

    public static void AddPolicy(RateLimiterOptions options, IConfiguration config, ClientIpResolver clientIp)
    {
        var enabled = config.GetValue("RateLimiting:AuthLogout:Enabled", true);
        var permit = Math.Max(1, config.GetValue("RateLimiting:AuthLogout:PermitLimit", DefaultPermitLimit));
        var window = Math.Max(1, config.GetValue("RateLimiting:AuthLogout:WindowSeconds", DefaultWindowSeconds));

        options.AddPolicy(Policy, httpContext =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter($"{Policy}:disabled");

            var user = httpContext.User.FindFirst("sub")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var partition = user is not null ? $"{Policy}|user|{user}" : $"{Policy}|ip|{clientIp.Resolve(httpContext)}";
            return RateLimitPartition.GetFixedWindowLimiter(partition, _ =>
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
