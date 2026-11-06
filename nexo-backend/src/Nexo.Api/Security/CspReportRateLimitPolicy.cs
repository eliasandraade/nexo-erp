using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Api.Middleware;

namespace Nexo.Api.Security;

/// <summary>
/// Reports per client address (<see cref="ClientIpResolver"/>), overridable via <c>RateLimiting:CspReport:*</c>. Over the limit the
/// report is dropped and the answer is the same 204: a browser ignores it, and the limit exists only to bound the log.
/// </summary>
public sealed class CspReportRateLimitPolicy : IRateLimiterPolicy<string>
{
    public const string Name = "csp-report";

    private const string Section = "RateLimiting:CspReport";
    private const int DefaultPermit = 30;
    private const int DefaultWindowSeconds = 60;

    private readonly ClientIpResolver _clientIp;
    private readonly bool _enabled;
    private readonly int _permit;
    private readonly TimeSpan _window;

    public CspReportRateLimitPolicy(ClientIpResolver clientIp, IConfiguration config)
    {
        _clientIp = clientIp;
        _enabled = config.GetValue($"{Section}:Enabled", true);
        _permit = config.GetValue($"{Section}:PermitLimit", DefaultPermit);
        _window = TimeSpan.FromSeconds(config.GetValue($"{Section}:WindowSeconds", DefaultWindowSeconds));
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        return ValueTask.CompletedTask;
    };

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        if (!_enabled) return RateLimitPartition.GetNoLimiter($"{Name}:disabled");

        return RateLimitPartition.GetFixedWindowLimiter($"{Name}|{_clientIp.Resolve(httpContext)}", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = _permit,
                Window               = _window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    }
}
