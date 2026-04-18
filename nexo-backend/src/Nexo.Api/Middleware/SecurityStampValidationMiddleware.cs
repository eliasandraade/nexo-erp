using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Middleware;

/// <summary>
/// Validates the SecurityStamp embedded in every access token against the
/// current value stored in the database (cached in Redis for 60 seconds).
///
/// When a platform admin calls "Revogar sessões" or "Resetar senha", the
/// user's SecurityStamp is rotated. The next request from any existing
/// token is rejected here — even before the 15-minute expiry window closes.
///
/// Flow:
///   1. Read "security_stamp" claim from JWT.
///   2. If absent → skip (backward compat with tokens issued before this change).
///   3. Check Redis cache (key: user:stamp:{userId}, TTL: 60s).
///   4. On cache miss: load from DB, populate cache.
///   5. If mismatch → 401 Session invalidated.
///
/// Must run AFTER UseAuthentication() and TenantResolutionMiddleware.
/// Platform tokens ("type" == "platform") are skipped.
/// </summary>
public class SecurityStampValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityStampValidationMiddleware> _logger;

    public SecurityStampValidationMiddleware(
        RequestDelegate next,
        ILogger<SecurityStampValidationMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Platform tokens don't have SecurityStamp — skip
        if (context.User.FindFirstValue("type") == "platform")
        {
            await _next(context);
            return;
        }

        var stampClaim = context.User.FindFirstValue("security_stamp");
        if (stampClaim is null)
        {
            // Token was issued before SecurityStamp was added — allow through
            await _next(context);
            return;
        }

        var userIdStr = context.User.FindFirstValue("userId");
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid user claim." });
            return;
        }

        var cache    = context.RequestServices.GetRequiredService<ICacheService>();
        var cacheKey = $"user:stamp:{userId}";

        var cached = await cache.GetAsync<StampCacheEntry>(cacheKey);

        if (cached is null)
        {
            var db = context.RequestServices.GetRequiredService<NexoDbContext>();
            var stamp = await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync();

            if (stamp is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "User not found." });
                return;
            }

            cached = new StampCacheEntry(stamp);
            await cache.SetAsync(cacheKey, cached, TimeSpan.FromSeconds(60));
        }

        if (cached.Stamp != stampClaim)
        {
            _logger.LogInformation(
                "SecurityStamp mismatch for user {UserId} — session revoked.",
                userId);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Session invalidated.", code = "session_revoked" });
            return;
        }

        await _next(context);
    }
}

internal record StampCacheEntry(string Stamp);
