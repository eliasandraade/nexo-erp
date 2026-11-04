using System.Security.Claims;
using Nexo.Infrastructure.Orken;

namespace Nexo.Api.Orken;

/// <summary>
/// Leitura do ERP pela Orken Platform (docs/engineering/orken-cockpit-contract.md). Um esquema JwtBearer próprio para tokens
/// de serviço do Orken ID, separado do JWT do ERP:
/// <list type="bullet">
/// <item>autoridade em <c>OrkenId:Issuer</c> (chaves e issuer lidos da descoberta OIDC);</item>
/// <item>audiência <c>orken-erp-api</c>, tipo <c>at+jwt</c>, claims sem mapeamento;</item>
/// <item>a política exige o escopo <c>erp.operations.read</c> e um <c>client_id</c> da lista <c>OrkenId:AllowedClients</c>
/// (padrão: <c>orken-platform-collector</c>).</item>
/// </list>
/// Sem <c>OrkenId:Issuer</c>, nada é registrado e a rota não existe (404). O token do ERP (tenant ou plataforma) não vale
/// aqui, e o token do Orken ID não vale em nenhuma outra rota do ERP: o esquema padrão continua sendo o JWT do ERP.
/// </summary>
public static class OrkenOperationsEndpoints
{
    public const string Scheme = "OrkenService";
    public const string Policy = "OrkenOperationsRead";
    public const string Audience = "orken-erp-api";
    public const string Scope = "erp.operations.read";
    public const string DefaultAllowedClient = "orken-platform-collector";
    public const string Route = "/api/orken/v1/operations";

    public static bool IsEnabled(IConfiguration configuration) => IssuerOf(configuration) is not null;

    private static Uri? IssuerOf(IConfiguration configuration) =>
        Uri.TryCreate(configuration["OrkenId:Issuer"], UriKind.Absolute, out var issuer) && issuer.Scheme is "https" or "http" ? issuer : null;

    public static void AddOrkenServiceAuthentication(this WebApplicationBuilder builder)
    {
        var issuer = IssuerOf(builder.Configuration);
        if (issuer is null) return;

        var allowed = builder.Configuration.GetSection("OrkenId:AllowedClients").Get<string[]>()?
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToHashSet(StringComparer.Ordinal) ?? [];
        if (allowed.Count == 0) allowed.Add(DefaultAllowedClient);

        builder.Services.AddAuthentication().AddJwtBearer(Scheme, o =>
        {
            o.Authority = issuer.ToString();
            o.Audience = Audience;
            o.MapInboundClaims = false;
            // Fora de desenvolvimento a descoberta e as chaves só por HTTPS.
            o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            o.TokenValidationParameters.ValidTypes = ["at+jwt"];
            o.TokenValidationParameters.NameClaimType = "sub";
            o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasScope(ctx.User, Scope) && ctx.User.FindFirstValue("client_id") is { } client && allowed.Contains(client))));
    }

    public static void MapOrkenOperations(this WebApplication app)
    {
        if (!IsEnabled(app.Configuration)) return;

        app.MapGet(Route, async (OrkenOperationsQuery query, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await query.SnapshotAsync(ct));
            })
            .RequireAuthorization(Policy)
            .ExcludeFromDescription();
    }

    /// <summary>O escopo vem como texto separado por espaço (RFC 9068) ou em várias claims; os dois formatos valem.</summary>
    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope, StringComparer.Ordinal);
}
