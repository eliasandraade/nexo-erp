using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Api.Middleware;
using Nexo.Application.Common.Interfaces;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// The doors of an account on the real API and a real PostgreSQL: creating one, verifying the e-mail, and what a token whose user
/// no longer exists gets. Each case answers with its own status and never a 500; nothing here may tell who has an account.
/// </summary>
[Collection("Integration")]
public class AccountEntryTests
{
    private readonly TestWebApplicationFactory _factory;
    public AccountEntryTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string Secret() => "Ax!" + Guid.NewGuid().ToString("N")[..20];   // a new one each run: no credential in the code
    private static string NewEmail() => $"entry{Guid.NewGuid():N}"[..20] + "@teste.dev";

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    // ── Registration ──────────────────────────────────────────────────────────

    public static TheoryData<string?, string?, string?, string> InvalidRegistrations => new()
    {
        { "A", "ok@teste.dev", "longenough", "Informe seu nome completo." },
        { "Nome Completo", "  ", "longenough", "Informe um e-mail válido." },
        { "Nome Completo", "ok@teste.dev", "12345", "A senha deve ter no mínimo 6 caracteres." },
    };

    [Theory]
    [MemberData(nameof(InvalidRegistrations))]
    public async Task An_invalid_registration_says_what_is_wrong_and_creates_nothing(string? name, string? email, string? password, string message)
    {
        var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register", new { name, email, password });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("error").GetString().Should().Be(message);
    }

    [Fact]
    public async Task An_email_already_registered_answers_409_with_its_code()
    {
        var email = NewEmail();
        var client = _factory.CreateApiClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { name = "Nome Completo", email, password = Secret() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await client.PostAsJsonAsync("/api/auth/register", new { name = "Outro Nome", email, password = Secret() });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyOf(again)).GetProperty("code").GetString().Should().Be("email_already_registered");
    }

    [Fact]
    public async Task An_account_not_yet_verified_cannot_sign_in_and_is_told_why()
    {
        var email = NewEmail();
        var password = Secret();
        var client = _factory.CreateApiClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { name = "Nome Completo", email, password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { login = email, password });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BodyOf(login)).GetProperty("code").GetString().Should().Be("email_not_verified");
    }

    // ── E-mail verification ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("%20")]
    [InlineData("token-that-was-never-issued")]
    public async Task A_bad_verification_link_answers_400_and_signs_nobody_in(string token)
    {
        var response = await _factory.CreateApiClient().GetAsync($"/api/auth/verify-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).TryGetProperty("accessToken", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_verification_token_nobody_issued_says_the_link_is_invalid_or_expired()
    {
        var response = await _factory.CreateApiClient().GetAsync("/api/auth/verify-email?token=token-that-was-never-issued");

        (await BodyOf(response)).GetProperty("error").GetString().Should().Be("Link inválido ou expirado.");
    }

    [Fact]
    public async Task Resending_needs_an_address()
    {
        var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/resend-verification", new { email = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resending_answers_the_same_whether_or_not_the_address_has_an_account()
    {
        var known = NewEmail();
        var client = _factory.CreateApiClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { name = "Nome Completo", email = known, password = Secret() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var forKnown = await client.PostAsJsonAsync("/api/auth/resend-verification", new { email = known });
        var forUnknown = await client.PostAsJsonAsync("/api/auth/resend-verification", new { email = NewEmail() });

        forKnown.StatusCode.Should().Be(HttpStatusCode.OK);
        forUnknown.StatusCode.Should().Be(HttpStatusCode.OK);
        (await forUnknown.Content.ReadAsStringAsync()).Should().Be(await forKnown.Content.ReadAsStringAsync());
    }

    // ── Manager check ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "any")]
    [InlineData("any", "  ")]
    public async Task The_manager_check_needs_login_and_password(string login, string password)
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/auth/verify-manager", new { login, password });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── The stamp check on its own, against the real table ────────────────────

    [Fact]
    public async Task The_stamp_check_refuses_a_person_with_no_row_and_stops_the_request()
    {
        using var scope = _factory.Services.CreateScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("security_stamp", "any-stamp"), new Claim("userId", Guid.NewGuid().ToString())], "Bearer")),
            Response = { Body = new MemoryStream() },
        };
        var reachedTheEndpoint = false;
        var middleware = new SecurityStampValidationMiddleware(_ =>
        {
            reachedTheEndpoint = true;
            return Task.CompletedTask;
        }, NullLogger<SecurityStampValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        reachedTheEndpoint.Should().BeFalse();
    }

    // ── A token that outlived its user ────────────────────────────────────────

    [Fact]
    public async Task A_valid_token_of_a_user_that_no_longer_exists_is_refused()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Sem Linha", email = NewEmail(), login = $"gone{Guid.NewGuid():N}"[..14], password = Secret(), role = "vendedor",
            phone = (string?)null, notes = (string?)null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var existing = await BodyOf(created);

        // A token signed the real way for a person whose row does not exist: the only thing wrong with it is the missing user.
        using var scope = _factory.Services.CreateScope();
        var ghost = Nexo.Domain.Entities.User.Create(existing.GetProperty("tenantId").GetGuid(), "Fantasma", NewEmail(),
            existing.GetProperty("login").GetString() + "x", "stored-hash", Nexo.Domain.Enums.UserRole.Vendedor);
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
            .GenerateTokenPair(ghost, "Empresa", "varejo", ["varejo"], Guid.Empty, []).AccessToken;

        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/categories");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BodyOf(response)).GetProperty("error").GetString().Should().Be("User not found.");
    }
}
