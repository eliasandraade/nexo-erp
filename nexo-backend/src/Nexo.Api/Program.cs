using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexo.Api.Middleware;
using Nexo.Application;
using Nexo.Infrastructure;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seed;
using Serilog;

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
           .WriteTo.Console(
               outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

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

    // ── JWT Authentication ────────────────────────────────────────────────────
    var jwtSecret = builder.Configuration["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

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
                // Only accept access tokens (audience "nexo-frontend") as Bearer.
                // Refresh tokens have audience "nexo-refresh" and are validated
                // exclusively by JwtTokenService.ValidateRefreshToken() inside
                // AuthService.RefreshAsync() — they never pass through this middleware.
                ValidAudience            = builder.Configuration["Jwt:Audience"] ?? "nexo-frontend",
                IssuerSigningKey         = new SymmetricSecurityKey(
                                               Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew                = TimeSpan.FromMinutes(1),
            };
        });

    builder.Services.AddAuthorization();

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("NexoFrontend", policy =>
        {
            var origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>()
                ?? ["http://localhost:3000", "http://localhost:8080", "http://localhost:5173"];

            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── Controllers ───────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "Nexo API",
            Version     = "v1",
            Description = "Backend for Nexo ERP — Gestão inteligente para empresas reais.",
        });

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

        if (!app.Environment.IsProduction())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
            await seeder.SeedAsync();
        }
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI(opts =>
        {
            opts.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexo API v1");
            opts.RoutePrefix = "swagger";
        });
    }

    app.UseCors("NexoFrontend");
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");

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
