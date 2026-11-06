using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Token com assinatura válida que a resolução do tenant precisa recusar. A recusa é da própria resolução,
/// com a resposta de erro dela: nenhum destes pedidos chega ao controller.
/// </summary>
[Collection("Integration")]
public class TenantResolutionRefusalTests
{
    private readonly TestWebApplicationFactory _factory;

    public TenantResolutionRefusalTests(TestWebApplicationFactory factory) => _factory = factory;

    /// <summary>Assinado com a mesma configuração de JWT do host de teste.</summary>
    private static string SignedToken(string userId, string tenantId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-minimum-32-characters-long!"));
        var token = new JwtSecurityToken(
            issuer: "nexo-api-test",
            audience: "nexo-frontend-test",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("userId", userId),
                new Claim("tenantId", tenantId),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> RefusalMessageAsync(string token)
    {
        using var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task Tenant_claim_that_is_not_an_id_is_refused()
    {
        var token = SignedToken(Guid.NewGuid().ToString(), "nao-e-um-id");

        (await RefusalMessageAsync(token)).Should().Be("Invalid tenant or user claim.");
    }

    [Fact]
    public async Task User_that_does_not_exist_is_refused()
    {
        var token = SignedToken(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        (await RefusalMessageAsync(token)).Should().Be("User not found.");
    }

    [Theory]
    [InlineData(UserStatus.Inactive)]
    [InlineData(UserStatus.Blocked)]
    public async Task User_that_is_not_active_is_refused_by_a_token_issued_for_it(UserStatus status)
    {
        var director = await SyntheticTenant.CreateAsync(_factory);
        var account = await SyntheticTenant.AddUserAsync(_factory, director, UserRole.Vendedor, status);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Login == account.Login);
            token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
                .GenerateTokenPair(user, "t", "T", [], Guid.Empty, []).AccessToken;
        }

        (await RefusalMessageAsync(token)).Should().Be("User account is inactive.");
    }
}
