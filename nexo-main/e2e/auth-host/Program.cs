using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

// Development/test only. The real API (controllers, middleware, CORS policy, rate limiter, migrations) on a disposable
// PostgreSQL (Testcontainers), behind a thin pass-through because the test server does not listen on a port. Same shape as
// e2e/backend-host, which seeds the portal and forwards only the portal routes; this one seeds nothing and forwards all of
// /api and the real-time hub. Test data only: every account is created by the suite, through the API, with synthetic values.
// Used by the authentication suite and by the Content-Security-Policy suite (e2e/csp.e2e.spec.ts).
//
// A second listener, with TLS and a certificate made here at startup, answers for *.orken.test: the shape of production, with
// the app and the API on sibling hosts of one site, and a third sibling that is not ours (playwright.auth.config.ts maps the names).
public static class AuthE2EHost
{
    const int ApiPort = 5196;
    const int ProductionShapePort = 5199;
    const string TestAddressHeader = "X-Forwarded-For";
    static readonly string[] FrontendOrigins = ["http://127.0.0.1:5197", "http://127.0.0.1:5198", $"https://erp.orken.test:{ProductionShapePort}"];

    /// <summary>The renewals of an address whose answer is held back, in milliseconds: a slow network, on demand, after the API has answered.</summary>
    static readonly ConcurrentDictionary<string, int> HeldRenewals = new();

    /// <summary>The names of the cookies that reached the last renewal of an address: what the server really got, whatever the browser reports.</summary>
    static readonly ConcurrentDictionary<string, string[]> CookiesOnRenewal = new();

    /// <summary>Connections to the hub through this host, so a test can drop them as a network would.</summary>
    static readonly ConcurrentDictionary<Guid, CancellationTokenSource> HubConnections = new();

    static readonly E2EStorage Storage = new();

    public static async Task Main(string[] args)
    {
        var root = Environment.GetEnvironmentVariable("AUTH_E2E_BACKEND_ROOT") ?? throw new InvalidOperationException("AUTH_E2E_BACKEND_ROOT required");
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_NEXO_API", Path.Combine(root, "src", "Nexo.Api"));
        await using var factory = new AuthFactory(root);
        await factory.InitializeAsync();

        // No cookie jar: it would be shared by every browser of the suite and send one test's cookies in another's requests.
        using var api = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        var certificate = NewCertificate();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, ApiPort);
            // HTTP/1.1, like the other listener: this pass-through copies headers that HTTP/2 does not take on an empty answer.
            kestrel.Listen(IPAddress.Loopback, ProductionShapePort, listen =>
            {
                listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
                listen.UseHttps(certificate);
            });
        });
        var app = builder.Build();
        app.UseWebSockets();

        // The app and the sibling that is not ours: a page to run a script from, and the sibling's attempts to write cookies for the whole site.
        app.Use(async (ctx, next) =>
        {
            var host = ctx.Request.Host.Host;
            if (host is "erp.orken.test" or "evil.orken.test")
            {
                if (ctx.Request.Path == "/tossed-cookies")
                {
                    var expires = DateTimeOffset.UtcNow.AddHours(1).ToString("R");
                    ctx.Response.Headers.Append("Set-Cookie", $"__Host-nexo_rt=do-vizinho; Domain=orken.test; Path=/; Secure; HttpOnly; SameSite=Lax; Expires={expires}");
                    ctx.Response.Headers.Append("Set-Cookie", $"__Host-nexo_rt=do-vizinho; Path=/; Secure; HttpOnly; SameSite=Lax; Expires={expires}");
                    ctx.Response.Headers.Append("Set-Cookie", $"nexo_rt=do-vizinho; Domain=orken.test; Path=/; Secure; HttpOnly; SameSite=Lax; Expires={expires}");
                }
                ctx.Response.ContentType = "text/html";
                await ctx.Response.WriteAsync("<!doctype html><title>orken.test</title>");
                return;
            }
            await next();
        });

        app.MapGet("/__test/setup", () => new
        {
            adminLogin = TestCredentials.AdminLogin,
            adminPassword = TestCredentials.AdminPassword,
            platformEmail = TestCredentials.PlatformEmail,
            platformPassword = TestCredentials.PlatformPassword,
        });

        // The same token, correctly signed, past its expiry: what the API sees 15 minutes after a sign-in, without the wait.
        app.MapPost("/__test/tokens/expired", (ExpireRequest request) =>
        {
            var config = factory.Services.GetRequiredService<IConfiguration>();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
            var token = new JwtSecurityTokenHandler().ReadJwtToken(request.AccessToken);
            string[] lifetime = ["exp", "nbf", "iat", "iss", "aud"];
            var now = DateTime.UtcNow;
            var expired = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: token.Claims.Where(c => !lifetime.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value)),
                notBefore: now.AddHours(-2),
                expires: now.AddHours(-1),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(expired) });
        });

        // What the e-mail would have carried: this host delivers no e-mail. A verification link, and the link of a new password.
        app.MapPost("/__test/links/verification", async (LinkRequest request) =>
        {
            var token = Guid.NewGuid().ToString("N");
            await ChangeUserAsync(factory, request.Email, user => user.SetVerificationToken(token, DateTime.UtcNow.AddMinutes(30)));
            return Results.Ok(new { token });
        });
        app.MapPost("/__test/links/password", async (LinkRequest request) =>
        {
            var token = Guid.NewGuid().ToString("N");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
            await ChangeUserAsync(factory, request.Email, user => user.SetPasswordResetToken(hash, DateTime.UtcNow.AddMinutes(30)));
            return Results.Ok(new { token });
        });

        // A second store: a company of the suite is born with one, and there is no screen to create another in the way of these tests.
        app.MapPost("/__test/stores", async (StoreRequest request) =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var tag = Guid.NewGuid().ToString("N")[..8];
            var store = Store.Create(request.TenantId, request.Name, $"loja-{tag}");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = store.Id });
        });

        app.MapPost("/__test/platform-users", async () =>
        {
            var email = $"e2e-{Guid.NewGuid():N}"[..16] + "@platform.test";
            var password = $"Sintetica-{Guid.NewGuid()}";
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<Nexo.Application.Common.Interfaces.IPasswordHasher>().Hash(password), "super_admin"));
            await db.SaveChangesAsync();
            return Results.Ok(new { email, password });
        });

        app.MapPost("/__test/hold-renewals", (HoldRequest request) =>
        {
            HeldRenewals[request.Address] = request.Milliseconds;
            return Results.NoContent();
        });

        // Puts a value in the cookie of the session, for what only another browser or another app could have done to it.
        app.MapGet("/__test/session-cookie", (string value, HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("Set-Cookie", $"__Host-nexo_rt={value}; Path=/; Secure; HttpOnly; SameSite=Lax; Expires={DateTimeOffset.UtcNow.AddHours(1):R}");
            return Results.NoContent();
        });

        app.MapGet("/__test/cookies-on-renewal", (string address) =>
            Results.Ok(new { names = CookiesOnRenewal.TryGetValue(address, out var names) ? names : [] }));

        app.MapPost("/__test/hub/drop", () =>
        {
            foreach (var connection in HubConnections.Values) connection.Cancel();
            return Results.NoContent();
        });

        // The object store as the browser sees it: the suite answers the storage hosts with these bytes, nothing leaves the machine.
        app.MapGet("/__test/media/{**key}", (string key) =>
            Storage.Inner.Objects.TryGetValue(key, out var value) ? Results.File(value.Bytes, value.ContentType) : Results.NotFound());

        // What the API wrote for the Content-Security-Policy reports it received, oldest first.
        app.MapGet("/__test/csp-reports", () => Results.Text(factory.ServerLog.Containing("CSP report")));

        // The queue of model files: the background job does not run in the test environment.
        app.MapPost("/__test/build/process-models", async () =>
        {
            using var scope = factory.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>();
            for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++) { }
            return Results.Ok();
        });

        // The hub: the negotiation is a plain request, and the browser's WebSocket is joined to one on the test server.
        app.Map("/hubs/{**path}", async ctx =>
        {
            if (ctx.WebSockets.IsWebSocketRequest) await TunnelAsync(ctx, factory.Server);
            else await ForwardAsync(ctx, api);
        });

        app.Map("/api/{**path}", ctx => ForwardAsync(ctx, api));

        Console.WriteLine($"Auth E2E host ready on http://127.0.0.1:{ApiPort} and *.orken.test:{ProductionShapePort} (test data only)");
        await app.RunAsync();
    }

    // Pass-through to the real API: method, headers (Origin, preflight headers, X-Forwarded-For) and body as sent by the
    // client; the response, CORS and Set-Cookie headers included, exactly as the API answered.
    static async Task ForwardAsync(HttpContext ctx, HttpClient api)
    {
        if (ctx.Request.Path == "/api/auth/refresh" && ctx.Request.Method == "POST")
            CookiesOnRenewal[ctx.Request.Headers[TestAddressHeader].ToString()] = [.. ctx.Request.Cookies.Keys];

        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), ctx.Request.Path + ctx.Request.QueryString);
        if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
            request.Content = new StreamContent(ctx.Request.Body);
        foreach (var (name, value) in ctx.Request.Headers)
        {
            if (name is "Host" or "Connection" or "Content-Length" or "Transfer-Encoding") continue;
            if (!request.Headers.TryAddWithoutValidation(name, value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(name, value.ToArray());
        }

        // Each test is its own client address, through X-Forwarded-For set on the browser context. In production the proxy writes that
        // header and no page can: the API does not allow it on a preflight. So it is the one header this host answers for by itself, and
        // the API judges all the others exactly as it would.
        var addressAsked = false;
        if (request.Headers.TryGetValues("Access-Control-Request-Headers", out var asked))
        {
            var names = asked.SelectMany(v => v.Split(',')).Select(n => n.Trim()).ToList();
            addressAsked = names.RemoveAll(n => n.Equals(TestAddressHeader, StringComparison.OrdinalIgnoreCase)) > 0;
            request.Headers.Remove("Access-Control-Request-Headers");
            if (names.Count > 0) request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", string.Join(",", names));
        }
        using var response = await api.SendAsync(request, ctx.RequestAborted);

        // The API already answered, and rotated the token: only the way back is slow.
        if (ctx.Request.Path == "/api/auth/refresh" && ctx.Request.Method == "POST"
            && HeldRenewals.TryGetValue(ctx.Request.Headers[TestAddressHeader].ToString(), out var hold))
            await Task.Delay(hold, CancellationToken.None);

        ctx.Response.StatusCode = (int)response.StatusCode;
        foreach (var h in response.Headers.Concat(response.Content.Headers))
            ctx.Response.Headers[h.Key] = h.Value.ToArray();
        ctx.Response.Headers.Remove("transfer-encoding");
        if (addressAsked && ctx.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
            ctx.Response.Headers.Append("Access-Control-Allow-Headers", TestAddressHeader);
        // A 204 or 304 has no body. Copying one, even empty, makes Kestrel throw after the status line, and then close the
        // connection the client had already put back in its keep-alive pool: the next request on it got "socket hang up".
        if (ctx.Response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status304NotModified) return;
        await response.Content.CopyToAsync(ctx.Response.Body);
    }

    static async Task TunnelAsync(HttpContext ctx, TestServer server)
    {
        var target = new UriBuilder(server.BaseAddress) { Scheme = "ws", Path = ctx.Request.Path, Query = ctx.Request.QueryString.Value }.Uri;
        using var upstream = await server.CreateWebSocketClient().ConnectAsync(target, ctx.RequestAborted);
        using var browser = await ctx.WebSockets.AcceptWebSocketAsync();
        using var dropped = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        var id = Guid.NewGuid();
        HubConnections[id] = dropped;
        try
        {
            await Task.WhenAny(PumpAsync(browser, upstream, dropped.Token), PumpAsync(upstream, browser, dropped.Token));
        }
        finally
        {
            HubConnections.TryRemove(id, out _);
            browser.Abort();
            upstream.Abort();
        }
    }

    static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
        {
            var read = await from.ReceiveAsync(buffer, ct);
            if (read.MessageType == WebSocketMessageType.Close)
            {
                await to.CloseAsync(read.CloseStatus ?? WebSocketCloseStatus.NormalClosure, read.CloseStatusDescription, ct);
                return;
            }
            await to.SendAsync(buffer.AsMemory(0, read.Count), read.MessageType, read.EndOfMessage, ct);
        }
    }

    static async Task ChangeUserAsync(AuthFactory factory, string email, Action<User> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        change(await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email));
        await db.SaveChangesAsync();
    }

    /// <summary>A certificate only for this process and these names. The browser of the suite is told to ignore that nobody signed it.</summary>
    static X509Certificate2 NewCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=orken.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("erp.orken.test");
        names.AddDnsName("api.erp.orken.test");
        names.AddDnsName("evil.orken.test");
        request.CertificateExtensions.Add(names.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        // Through PFX: on Windows the TLS stack does not take an ephemeral key.
        return new X509Certificate2(created.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
    }

    sealed record ExpireRequest(string AccessToken);
    sealed record LinkRequest(string Email);
    sealed record StoreRequest(Guid TenantId, string Name);
    sealed record HoldRequest(string Address, int Milliseconds);

    /// <summary>The real upload pipeline over memory, with Cloudflare R2's hosts in its URLs: the ones the production policy allows.</summary>
    sealed class E2EStorage : IStorageProvider
    {
        const string PublicBase = "https://orken-e2e.r2.dev/";
        const string SignedBase = "https://orken-e2e.r2.cloudflarestorage.com/";
        public InMemoryStorageProvider Inner { get; } = new();

        public async Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default)
        {
            var stored = await Inner.UploadAsync(request, ct);
            return new StorageUploadResult(stored.Key, PublicBase + request.ObjectKey);
        }

        public Task DeleteAsync(string key, CancellationToken ct = default) => Inner.DeleteAsync(key, ct);
        public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Inner.OpenReadAsync(key, ct);
        public string GetSignedUrl(string key, TimeSpan ttl) => $"{SignedBase}{key}?X-Amz-Signature=e2e&X-Amz-Expires={(int)ttl.TotalSeconds}";
    }

    sealed class AuthFactory(string root) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseContentRoot(Path.Combine(root, "src", "Nexo.Api"));
            // the frontend under test is a separate origin, as in production: the API's own CORS policy must let it in
            for (var i = 0; i < FrontendOrigins.Length; i++)
            {
                builder.UseSetting($"Cors:AllowedOrigins:{i}", FrontendOrigins[i]);
                builder.UseSetting($"Auth:Session:AllowedOrigins:{i}", FrontendOrigins[i]);
            }
            // the base factory turns the sign-in and renewal limits off for the shared integration suite; here they are under
            // test, at the production defaults, with each test of the suite as its own client address (X-Forwarded-For)
            builder.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthLogout:Enabled", "true");
            // the browsers of the suite share one client address, and a run sends more reports than a person would
            builder.UseSetting("RateLimiting:CspReport:Enabled", "false");
            builder.UseSetting("Integrations:Features:StorageEnabled", "true");
            builder.UseSetting("Integrations:Storage:R2:PublicUrl", "https://orken-e2e.r2.dev");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStorageProvider>();
                services.AddSingleton<IStorageProvider>(Storage);
            });
        }
    }
}
