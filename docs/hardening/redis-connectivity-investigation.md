# Redis Connectivity / Cache Effectiveness — Investigation

**Date:** 2026-06-16
**Status:** Root cause hypotheses ranked; safe code fix applied; production verification needs Railway access (your authorization).
**Scope:** Restore Redis cache effectiveness in production without re-introducing per-request stalls.

---

## 1. Current state (validated)

- The **circuit breaker works** (`RedisCacheService` + `Program.cs` warmup). When Redis is
  unreachable the app stays fast: calls short-circuit (~0 ms) and serve from the DB (fail-open).
- The cache is therefore **degraded, not broken** — correctness is fine, but every request that
  could be cached (tenant info, module lists, dashboard summary, **refresh-token validity**,
  security stamp) hits Postgres instead of Redis.
- Locally and in the integration test host there is **no Redis** (`ConnectionStrings:Redis` is
  empty) → `NoOpCacheService`. That masked a real refresh bug — see §6.

The connection is configured from the env var **`ConnectionStrings__Redis`** (Railway).
`appsettings.json` ships it empty.

## 2. Most likely root cause (ranked)

### #1 — Connection string is a `redis://` URI that SE.Redis can't parse  ← fix applied
Railway (and Upstash/Heroku/Render) expose Redis as a **URI**:

```
redis://default:<password>@redis.railway.internal:6379
rediss://default:<password>@<public-host>:<port>      # TLS via public proxy
```

`StackExchange.Redis.ConfigurationOptions.Parse(...)` does **not** understand the URI scheme.
If `ConnectionStrings__Redis` holds a `redis://…` value, the multiplexer never connects and the
cache is degraded **forever** — exactly the observed symptom. This is the single most common
cause of "Redis unreachable on Railway".

**Fix applied (this branch):** `RedisConfiguration.BuildOptions` now accepts BOTH the native
SE.Redis format and `redis://` / `rediss://` URIs. Non-URI strings pass through unchanged, so it
is safe regardless of which format the env var currently holds. Covered by 7 unit tests.

### #2 — Private networking host not reachable (IPv6 / same-project-and-environment)
Railway private DNS `*.railway.internal` resolves only **inside the same project & environment**
and is **IPv6-only**. If the backend and the Redis service are in different projects/environments,
or the multiplexer resolves IPv4 only, the internal host is unreachable.
- Mitigation A: ensure backend + Redis are in the **same project and environment**, use
  `redis.railway.internal:6379`.
- Mitigation B: if private networking can't be used, switch to the Redis **public TCP proxy**
  endpoint (`rediss://…<proxy-host>:<port>`, TLS) — the URI handling from #1 covers this.

### #3 — TLS required but not enabled
The public proxy typically needs TLS. With a native (non-URI) string lacking `ssl=true`, the
handshake fails. Using the `rediss://` URI (or appending `,ssl=true`) fixes it — handled by #1.

### #4 — Wrong credentials / user
Redis 6+ uses ACLs; Railway uses the `default` user + password (AUTH with the password alone is
enough). A stale password in the env var fails AUTH. The URI parser decodes percent-encoded
credentials and only sets a non-`default` `User`.

## 3. Read-only diagnostics for you to run (do NOT paste secrets back here)

```bash
# 1) What format / host is configured? (LOOK at the scheme & host — do not share the value)
railway variables            # inspect ConnectionStrings__Redis (or REDIS_URL)

# 2) Are backend + Redis in the same project/environment?
railway status

# 3) From a backend shell on Railway, can it resolve/reach Redis?
railway run -- sh -lc 'getent hosts redis.railway.internal; nc -zv redis.railway.internal 6379'

# 4) After deploying this branch, confirm the multiplexer connects:
#    look for "Redis connection warmed." (success) vs the warmup warning (still failing)
railway logs | grep -i redis
```

Decision tree:
- Value starts with `redis://`/`rediss://` → **deploy this branch** (#1). Most likely.
- Internal host unreachable (`nc` fails) → fix project/environment or move to public proxy (#2).
- `WRONGPASS`/auth error in logs → rotate/copy the correct password (#4).

## 4. The code change in this branch

- `src/Nexo.Infrastructure/Cache/RedisConfiguration.cs` — new `BuildOptions(connectionString)`.
- `src/Nexo.Infrastructure/DependencyInjection.cs` — uses it instead of `ConfigurationOptions.Parse`.
- `tests/Nexo.UnitTests/Cache/RedisConfigurationTests.cs` — 7 tests (native, URI, TLS, default
  vs non-default user, password-only, default port).

**Risk:** very low. Behaviour is **unchanged** for native-format strings; it only ADDS support for
the URI form that currently fails. The fail-open posture (`AbortOnConnectFail=false`, timeouts) and
the circuit breaker are preserved verbatim, so a misconfigured Redis still cannot stall requests.

**Rollback:** revert the commit (or the PR). No data/schema/runtime state is touched; the previous
behaviour was "URI strings fail to connect (degraded cache)", which the fail-open path already
handles safely.

## 5. What needs your authorization

- **Deploying** this branch to production (you control deploys).
- Any change to **`ConnectionStrings__Redis`** / Railway networking (env/secret change).
- I did **not** run `railway variables`/`logs` myself — those print the secret connection string,
  and the brief forbids exposing secrets. Run the read-only commands in §3 yourself.

## 6. Related: refresh-token bug this investigation surfaced

With Redis degraded (or NoOp in tests) the refresh-token validity entry was never read back, so
refresh returned 401 and **masked a second bug**: `/api/auth/refresh` is anonymous, so
`RefreshAsync` loaded the user through the tenant query filter (`CurrentTenantIdForFilter ==
Guid.Empty`) and found nothing → 401 even when Redis is healthy. Fixed in
`fix/auth-session-hardening` (`GetByIdAcrossTenantsAsync` + tenant-match assertion). **Once Redis
is restored, that fix is what makes refresh actually succeed** — deploy both together.

## 7. Expected outcome after the fix + correct env

- `Redis connection warmed.` on startup.
- Cache hit/miss working; tenant/module/dashboard reads served from Redis.
- No per-request timeout (circuit breaker unchanged).
- Refresh-token validity + security-stamp reads no longer hit the DB on every request.
