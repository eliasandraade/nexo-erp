using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Infrastructure.Modules.Build;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

// Development/test only. The real API (controllers, middleware, CORS policy, rate limiter, migrations) on a disposable
// PostgreSQL (Testcontainers), behind a thin pass-through because the test server does not listen on a port. Same shape as
// e2e/backend-host, which seeds the portal and forwards only the portal routes; this one seeds nothing and forwards all of
// /api and the real-time hub. Test data only: every account is created by the suite, through the API, with synthetic values.
// Used by the authentication suite and by the Content-Security-Policy suite (e2e/csp.e2e.spec.ts).
public static class AuthE2EHost
{
    const string ApiUrl = "http://127.0.0.1:5196";
    const string FrontendOrigin = "http://127.0.0.1:5197";
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
        builder.WebHost.UseUrls(ApiUrl);
        var app = builder.Build();
        app.UseWebSockets();

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

        Console.WriteLine($"Auth E2E host ready on {ApiUrl} (test data only)");
        await app.RunAsync();
    }

    // Pass-through to the real API: method, headers (Origin, preflight headers, X-Forwarded-For) and body as sent by the
    // client; the response, CORS and Set-Cookie headers included, exactly as the API answered.
    static async Task ForwardAsync(HttpContext ctx, HttpClient api)
    {
        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), ctx.Request.Path + ctx.Request.QueryString);
        if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
            request.Content = new StreamContent(ctx.Request.Body);
        foreach (var (name, value) in ctx.Request.Headers)
        {
            if (name is "Host" or "Connection" or "Content-Length" or "Transfer-Encoding") continue;
            if (!request.Headers.TryAddWithoutValidation(name, value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(name, value.ToArray());
        }
        using var response = await api.SendAsync(request, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)response.StatusCode;
        foreach (var h in response.Headers.Concat(response.Content.Headers))
            ctx.Response.Headers[h.Key] = h.Value.ToArray();
        ctx.Response.Headers.Remove("transfer-encoding");
        await response.Content.CopyToAsync(ctx.Response.Body);
    }

    static async Task TunnelAsync(HttpContext ctx, TestServer server)
    {
        var target = new UriBuilder(server.BaseAddress) { Scheme = "ws", Path = ctx.Request.Path, Query = ctx.Request.QueryString.Value }.Uri;
        using var upstream = await server.CreateWebSocketClient().ConnectAsync(target, ctx.RequestAborted);
        using var browser = await ctx.WebSockets.AcceptWebSocketAsync();
        await Task.WhenAny(PumpAsync(browser, upstream, ctx.RequestAborted), PumpAsync(upstream, browser, ctx.RequestAborted));
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

    sealed record ExpireRequest(string AccessToken);

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
            builder.UseSetting("Cors:AllowedOrigins:0", FrontendOrigin);
            // the base factory turns the sign-in and renewal limits off for the shared integration suite; here they are under
            // test, at the production defaults, with each test of the suite as its own client address (X-Forwarded-For)
            builder.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
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
