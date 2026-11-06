using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

// Development/test only. The real API (controllers, middleware, CORS policy, rate limiter, migrations) on a disposable
// PostgreSQL (Testcontainers), behind a thin pass-through because the test server does not listen on a port. Same shape as
// e2e/backend-host, which seeds the portal and forwards only the portal routes; this one seeds nothing and forwards all of
// /api. Test data only: every account is created by the suite, through the API, with synthetic values.
public static class AuthE2EHost
{
    const string ApiUrl = "http://127.0.0.1:5196";
    const string FrontendOrigin = "http://127.0.0.1:5197";

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

        // Pass-through to the real API: method, headers (Origin, preflight headers, X-Forwarded-For) and body as sent by the
        // client; the response, CORS and Set-Cookie headers included, exactly as the API answered.
        app.Map("/api/{**path}", async ctx =>
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
        });

        Console.WriteLine($"Auth E2E host ready on {ApiUrl} (test data only)");
        await app.RunAsync();
    }

    sealed record ExpireRequest(string AccessToken);

    sealed class AuthFactory(string root) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseContentRoot(Path.Combine(root, "src", "Nexo.Api"));
            // the frontend under test is a separate origin, as in production: the API's own CORS policy must let it in
            builder.UseSetting("Cors:AllowedOrigins:0", FrontendOrigin);
            // the base factory turns the sign-in limiter off for the shared integration suite; here it is under test, at
            // the production defaults, with each test of the suite as its own client address (X-Forwarded-For)
            builder.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
        }
    }
}
