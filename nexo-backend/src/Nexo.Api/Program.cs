using System.IO.Compression;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexo.Api.Middleware;
using Nexo.Application;
using Nexo.Infrastructure;
using Nexo.Infrastructure.Hubs;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seed;
using Serilog;
using StackExchange.Redis;

// ── Bootstrap Serilog early so startup failures are logged ───────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.With(new Nexo.Api.Observability.RequestIdEnricher(services.GetRequiredService<IHttpContextAccessor>()));

        // Structured logs: one JSON object per line outside development (RequestId, TenantId,
        // UserId, Route… become searchable fields in the platform's log viewer); readable text in dev.
        if (ctx.HostingEnvironment.IsDevelopment())
            cfg.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {RequestId}{NewLine}{Exception}");
        else
            cfg.WriteTo.Console(Nexo.Api.Observability.RequestLogging.JsonFormatter());

        if (!ctx.HostingEnvironment.IsDevelopment())
        {
            cfg.WriteTo.File(
                path: "logs/nexo-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);
        }
    });

    // ── Application layers ────────────────────────────────────────────────────
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    Nexo.Api.Observability.OpenTelemetrySetup.AddOrkenTelemetry(builder.Services, builder.Configuration, builder.Environment);
    // Títulos vencidos: hourly Pending → Overdue. Service customer e-mails: confirmations and
    // reminders every two minutes. Not in Testing (tests call the jobs' passes directly).
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddHostedService<Nexo.Infrastructure.Jobs.OverdueTitlesJob>();
        builder.Services.AddHostedService<Nexo.Infrastructure.Jobs.ServiceCustomerEmailsJob>();
        builder.Services.AddHostedService<Nexo.Infrastructure.Jobs.BuildProgressSnapshotJob>();
        // Re-seals AI provider keys still in the legacy Base64 form once a master key is configured (after migrations).
        builder.Services.AddHostedService<Nexo.Infrastructure.Modules.Interpreter.AiKeyUpgradeHostedService>();
    }

    // ── JWT Authentication ────────────────────────────────────────────────────
    var jwtSecret = builder.Configuration["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
    // Fail fast outside Development/Testing when the secret is missing, short, a committed
    // placeholder or not random — otherwise anyone with the repo could forge tokens.
    Nexo.Infrastructure.Auth.JwtSecretPolicy.EnsureSafe(jwtSecret, builder.Environment.EnvironmentName);

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = builder.Configuration["Jwt:Issuer"]   ?? "nexo-api",
                // Accept tenant access tokens ("nexo-frontend") and platform admin
                // tokens ("nexo-platform"). Refresh tokens ("nexo-refresh") are
                // validated exclusively by JwtTokenService.ValidateRefreshToken().
                ValidAudiences           = new[]
                {
                    builder.Configuration["Jwt:Audience"]         ?? "nexo-frontend",
                    builder.Configuration["Jwt:PlatformAudience"] ?? "nexo-platform",
                },
                IssuerSigningKey         = new SymmetricSecurityKey(
                                               Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew                = TimeSpan.FromMinutes(1),
            };
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    // SignalR sends JWT via query string for WebSocket connections
                    var accessToken = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        ctx.Token = accessToken;

                    // Cookie fallback: only read nexo_access cookie when there is no
                    // Authorization header. The header always wins — the cookie is a
                    // legacy fallback for environments that can't send the header.
                    if (string.IsNullOrEmpty(ctx.Token)
                        && !ctx.Request.Headers.ContainsKey("Authorization"))
                    {
                        var cookieToken = ctx.Request.Cookies["nexo_access"];
                        if (!string.IsNullOrEmpty(cookieToken))
                            ctx.Token = cookieToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        // Platform super-admin gate: must be authenticated AND carry the platform token
        // claim (type=platform, set by JwtTokenService.GeneratePlatformToken). Applied at
        // the controller level on the /api/platform/* controllers so a new endpoint can't
        // forget the check. Unauthenticated → 401; authenticated non-platform → 403.
        options.AddPolicy("Platform", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("type", "platform"));
        // Platform administrators only (super_admin): acts that change what tenants can do (activation, approvals, catalogues).
        // Admin acts also require a sign-in that passed two-step verification (Platform:RequireMfaForAdmin, default true):
        // a stolen password alone can't change tenants, keys or impersonate.
        var adminNeedsMfa = builder.Configuration.GetValue("Platform:RequireMfaForAdmin", true);
        options.AddPolicy("PlatformAdmin", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("type", "platform").RequireRole("super_admin")
                  .RequireAssertion(ctx => !adminNeedsMfa || ctx.User.HasClaim("platform_mfa", "true")));
    });

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("NexoFrontend", policy =>
        {
            // Always include known origins; Railway env Cors:AllowedOrigins can add more.
            var builtInOrigins = new[]
            {
                "http://localhost:3000",
                "http://localhost:8080",
                "http://localhost:5173",
                "https://app.orken.com.br",
                "https://www.orken.com.br",
            };

            var configOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            var origins = builtInOrigins.Concat(configOrigins).Distinct().ToArray();

            policy.WithOrigins(origins)
                  .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
                  .AllowAnyHeader()
                  .AllowCredentials()
                  // Cache the CORS preflight (OPTIONS) for 24h. The SPA lives on a
                  // different origin (app.orken.com.br) than the API, so without this
                  // every non-simple request was preceded by a fresh OPTIONS round-trip,
                  // doubling latency on the cold first load.
                  .SetPreflightMaxAge(TimeSpan.FromHours(24))
                  .WithExposedHeaders("Content-Disposition", "Content-Length", "Retry-After", Nexo.Api.Middleware.CorrelationIdMiddleware.Header);
        });
    });

    // ── Response Compression ───────────────────────────────────────────────────
    // JSON payloads (sales/customers/product lists, dashboard summary) were sent
    // uncompressed. Brotli/Gzip cut transfer size ~70-80% on the first load.
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            new[] { "application/json", "application/json; charset=utf-8" });
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
    builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

    // ── Rate Limiting ────────────────────────────────────────────────────────
    // "auth-login" protects /auth/login and /auth/refresh from brute force.
    //
    // Config-driven (RateLimiting:AuthLogin:{Enabled,PermitLimit,WindowSeconds})
    // so the integration suite can disable it (the previous global limiter caused
    // a shared-window 429 cascade across the whole suite), while the dedicated
    // RateLimitingTests re-enable it explicitly.
    //
    // PARTITIONED per (client IP + endpoint path) — the previous
    // AddFixedWindowLimiter("auth-login") used a SINGLE GLOBAL bucket, so 5 failed
    // logins from anyone locked out every user for 15 min. Now each client gets
    // its own budget, and login vs refresh are counted separately.
    var authLoginEnabled = builder.Configuration.GetValue("RateLimiting:AuthLogin:Enabled", true);
    var authLoginPermit  = builder.Configuration.GetValue("RateLimiting:AuthLogin:PermitLimit", 5);
    var authLoginWindow  = builder.Configuration.GetValue("RateLimiting:AuthLogin:WindowSeconds", 900);

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy("auth-login", httpContext =>
        {
            if (!authLoginEnabled)
                return RateLimitPartition.GetNoLimiter("auth-login:disabled");

            // Prefer the client IP from X-Forwarded-For (Railway sits behind a
            // proxy); fall back to the socket address. NOTE: a client could spoof
            // X-Forwarded-For — to harden, configure ForwardedHeaders middleware
            // with KnownProxies and partition on the resolved RemoteIpAddress.
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var partitionKey = $"{clientIp}|{httpContext.Request.Path}";

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = authLoginPermit,
                    Window               = TimeSpan.FromSeconds(authLoginWindow),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                });
        });

        // Authenticated credential checks (change own password, manager override). Partitioned by
        // the signed-in user — unforgeable, unlike X-Forwarded-For, and several PDV terminals of the
        // same store behind one IP don't throttle each other. Overridable via RateLimiting:CredentialCheck:*.
        var credEnabled = builder.Configuration.GetValue("RateLimiting:CredentialCheck:Enabled", true);
        var credPermit  = builder.Configuration.GetValue("RateLimiting:CredentialCheck:PermitLimit", 10);
        var credWindow  = builder.Configuration.GetValue("RateLimiting:CredentialCheck:WindowSeconds", 300);

        options.AddPolicy("credential-check", httpContext =>
        {
            if (!credEnabled)
                return RateLimitPartition.GetNoLimiter("credential-check:disabled");

            var userId = httpContext.User.FindFirst("sub")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter($"cred|{userId}|{httpContext.Request.Path}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = credPermit,
                    Window               = TimeSpan.FromSeconds(credWindow),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                });
        });

        // Public booking POST (portal de agendamento) — per-IP fixed window so an abusive client
        // can't flood appointment creation. Defaults overridable via RateLimiting:PublicBooking:*.
        var pubEnabled = builder.Configuration.GetValue("RateLimiting:PublicBooking:Enabled", true);
        var pubPermit  = builder.Configuration.GetValue("RateLimiting:PublicBooking:PermitLimit", 15);
        var pubWindow  = builder.Configuration.GetValue("RateLimiting:PublicBooking:WindowSeconds", 60);

        options.AddPolicy("public-booking", httpContext =>
        {
            if (!pubEnabled)
                return RateLimitPartition.GetNoLimiter("public-booking:disabled");

            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter($"pub-booking|{clientIp}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = pubPermit,
                    Window               = TimeSpan.FromSeconds(pubWindow),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                });
        });
    });

    // Public restaurant portal (pedido, cupom): per client IP, so one caller can't flood orders or
    // brute-force coupon codes. Defaults overridable via RateLimiting:PublicOrders:*.
    builder.Services.Configure<RateLimiterOptions>(options =>
    {
        var enabled = builder.Configuration.GetValue("RateLimiting:PublicOrders:Enabled", true);
        var permit  = builder.Configuration.GetValue("RateLimiting:PublicOrders:PermitLimit", 20);
        var window  = builder.Configuration.GetValue("RateLimiting:PublicOrders:WindowSeconds", 60);

        options.AddPolicy("public-orders", httpContext =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter("public-orders:disabled");

            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter($"pub-orders|{clientIp}", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = permit,
                    Window               = TimeSpan.FromSeconds(window),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                });
        });
    });

    // Orken Build client portal (public, per client IP): "build-portal" for reads, "build-portal-auth" for
    // password attempts (on top of the per-portal lock after 20 wrong passwords). Overridable via
    // RateLimiting:BuildPortal:* and RateLimiting:BuildPortalAuth:*.
    builder.Services.Configure<RateLimiterOptions>(options =>
    {
        void AddIpPolicy(string policy, string section, int defaultPermit, int defaultWindow)
        {
            var enabled = builder.Configuration.GetValue($"RateLimiting:{section}:Enabled", true);
            var permit  = builder.Configuration.GetValue($"RateLimiting:{section}:PermitLimit", defaultPermit);
            var window  = builder.Configuration.GetValue($"RateLimiting:{section}:WindowSeconds", defaultWindow);

            options.AddPolicy(policy, httpContext =>
            {
                if (!enabled)
                    return RateLimitPartition.GetNoLimiter($"{policy}:disabled");

                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                    ? forwardedFor.Split(',')[0].Trim()
                    : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{clientIp}", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = permit,
                        Window               = TimeSpan.FromSeconds(window),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit           = 0,
                    });
            });
        }

        AddIpPolicy("build-portal", "BuildPortal", 120, 60);
        AddIpPolicy("build-portal-auth", "BuildPortalAuth", 10, 300);
        // client decisions (change orders, selections): writes, so tighter than reads
        AddIpPolicy("build-portal-decision", "BuildPortalDecision", 30, 300);
    });

    // ── Controllers ───────────────────────────────────────────────────────────
    // JsonStringEnumConverter: serializes all enums as their string names
    // ("Planning" instead of 0, "Draft" instead of 0, etc.).
    // Consistent with the restaurante pattern where services map Status.ToString().
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            // Serialize responses in camelCase so the frontend (TypeScript) can read them
            // without needing PascalCase field access (e.g. data.accessToken, not data.AccessToken).
            o.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;

            // Accept camelCase request bodies from the frontend (e.g. { "refreshToken": "..." }
            // binds correctly to RefreshTokenRequest.RefreshToken).
            o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;

            // Serialize enums as their string names ("Planning", not 0).
            o.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "Orken API",
            Version     = "v1",
            Description = "Backend for Nexo ERP — Gestão inteligente para empresas reais.",
        });

        // Nested request records (e.g. PlatformController.ResetPasswordRequest) clashed with top-level
        // types of the same name and broke the whole document; nested types are now qualified.
        opts.CustomSchemaIds(Nexo.Api.OpenApiSchemaIds.For);

        // Enable Bearer token in Swagger UI
        opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            Description  = "Paste your JWT token here.",
        });

        opts.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id   = "Bearer",
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Run migrations and seed on startup ────────────────────────────────────
    // Skipped in "Testing" — the WebApplicationFactory.InitializeAsync handles
    // migrations and seeding against the Testcontainers database directly.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        Log.Information("Applying database migrations...");
        await db.Database.MigrateAsync();

        // Platform super-admin bootstrap: idempotent, env-driven (Seed:Platform*).
        // Runs in EVERY environment INCLUDING Production — decoupled from the demo
        // seeder so production gets a working super-admin without demo tenant data.
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedPlatformAdminAsync();

        if (!app.Environment.IsProduction())
        {
            await seeder.SeedAsync();   // demo tenants/users — non-production only
        }
    }

    // ── Warm the Redis connection at startup (non-blocking) ───────────────────
    // Nudge the multiplexer to connect so the first authenticated request doesn't
    // pay the connect cost. Runs in the BACKGROUND with a short cap: a blocking
    // ping cost ~5s on cold start when Redis was unreachable. AbortOnConnectFail
    // =false means the multiplexer reconnects on its own regardless; this just
    // warms it and logs the outcome without delaying startup.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var mux = app.Services.GetService<IConnectionMultiplexer>();
        if (mux is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await mux.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    Log.Information("Redis connection warmed.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex,
                        "Redis warmup ping failed — cache degraded, multiplexer will reconnect in background (fail-open).");
                }
            });
        }
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    // Response compression first so it wraps every downstream response body.
    app.UseResponseCompression();

    // CORS next so every response (including errors and rate-limit
    // rejections) carries the Access-Control-Allow-Origin header.
    app.UseCors("NexoFrontend");

    app.UseMiddleware<CorrelationIdMiddleware>();
    // request log outside the exception handler: it records the status the client really got
    Nexo.Api.Observability.RequestLogging.UseOrkenRequestLogging(app);
    app.UseMiddleware<ExceptionHandlingMiddleware>();


    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI(opts =>
        {
            opts.SwaggerEndpoint("/swagger/v1/swagger.json", "Orken API v1");
            opts.RoutePrefix = "swagger";
        });
    }

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseMiddleware<SecurityStampValidationMiddleware>();
    app.UseAuthorization();

    // Security headers
    app.Use(async (context, next) =>
    {
        // Prevent clickjacking
        context.Response.Headers["X-Frame-Options"] = "DENY";
        
        // Prevent MIME type sniffing
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        
        // Referrer policy
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        
        // Restrict permissions
        context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=(), payment=()";
        
        // Content Security Policy - permissive enough for app functionality
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";
        
        await next();
    });
    app.MapHub<RestaurantHub>("/hubs/restaurant");
    app.MapControllers();
    Nexo.Api.Health.HealthEndpoints.MapOrkenHealth(app);

    Log.Information("Nexo API starting on {Urls}", string.Join(", ", app.Urls));
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Nexo API failed to start.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
