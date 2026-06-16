using StackExchange.Redis;

namespace Nexo.Infrastructure.Cache;

/// <summary>
/// Builds <see cref="ConfigurationOptions"/> for StackExchange.Redis from a connection string.
///
/// Accepts BOTH formats:
///   1. Native SE.Redis:  "host:port,password=secret,ssl=true,..."
///   2. URI:              "redis://[user:password@]host:port[/db]"  (or "rediss://" for TLS)
///
/// Why this exists: Railway — and Upstash, Heroku, Render and most managed Redis providers —
/// expose the connection as a URI (REDIS_URL). <see cref="ConfigurationOptions.Parse"/> does
/// NOT understand the URI scheme, so a raw "redis://…" value fails to connect and the cache
/// silently runs degraded (fail-open) forever. Converting the URI here is the single most
/// common fix for "Redis unreachable on Railway". Non-URI strings pass through unchanged, so
/// this is safe for environments already using the native format.
/// </summary>
public static class RedisConfiguration
{
    public static ConfigurationOptions BuildOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Redis connection string is empty.", nameof(connectionString));

        var trimmed = connectionString.Trim();

        var options = IsUri(trimmed)
            ? FromUri(trimmed)
            : ConfigurationOptions.Parse(trimmed);

        // Fail-open posture (kept identical to the previous inline configuration): never block
        // a request on a dead Redis. The circuit breaker in RedisCacheService short-circuits
        // calls while it is down, and AbortOnConnectFail=false lets the multiplexer reconnect
        // in the background so caching resumes automatically once connectivity is restored.
        options.AbortOnConnectFail = false;
        options.ConnectTimeout     = 3000;
        options.AsyncTimeout       = 2500;
        return options;
    }

    private static bool IsUri(string value)
        => value.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

    private static ConfigurationOptions FromUri(string uriString)
    {
        var uri = new Uri(uriString);

        var options = new ConfigurationOptions
        {
            // rediss:// → TLS. Railway's public proxy and Upstash require this.
            Ssl = uriString.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase),
        };
        options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : 6379);

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var credentials = uri.UserInfo.Split(':', 2);
            if (credentials.Length == 2)
            {
                // "default" is the conventional Redis 6+ ACL user — AUTH with just the password
                // works for it, so we only set User for a genuinely non-default ACL user.
                var user = Uri.UnescapeDataString(credentials[0]);
                if (!string.IsNullOrEmpty(user) && !string.Equals(user, "default", StringComparison.OrdinalIgnoreCase))
                    options.User = user;

                options.Password = Uri.UnescapeDataString(credentials[1]);
            }
            else
            {
                // password-only form: redis://:password@host
                options.Password = Uri.UnescapeDataString(credentials[0]);
            }
        }

        return options;
    }
}
