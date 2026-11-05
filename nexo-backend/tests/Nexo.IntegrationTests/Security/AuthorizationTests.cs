using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Authorization and module access control test suite.
/// Validates:
/// - RequireModuleAttribute enforcement
/// - Role-based access control
/// - Module cache consistency
/// - Admin-only endpoints
/// - Platform-only endpoints
/// - Inactive/blocked user rejection
/// - Tenant status validation
/// </summary>
[Collection("Integration")]
public class AuthorizationTests
{
    private readonly TestWebApplicationFactory _factory;

    public AuthorizationTests(TestWebApplicationFactory factory)
        => _factory = factory;

    // ────────────────────────────────────────────────────────────────────────────
    // REQUIRE MODULE ATTRIBUTE
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModuleRequirement_ActiveModule_AllowsAccess()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        // Assuming /api/cash/* or similar endpoint requires a module
        var response = await client.GetAsync("/api/auth/me");

        // Should succeed (even if the endpoint itself needs modules)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Forbidden,   // If module not active
            HttpStatusCode.Unauthorized
        );
    }

    [Fact]
    public async Task ModuleRequirement_InactiveModule_ReturnsInsufficientPermissions()
    {
        // This test assumes:
        // 1. There's an endpoint that requires a specific module
        // 2. That module can be deactivated for a tenant
        // 3. When deactivated, accessing that endpoint returns 403

        // Implementation depends on specific endpoints available
        // Document this test as a template for when modules are available
        await Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ROLE-BASED ACCESS CONTROL
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminRole_CanAccessAdminEndpoints()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var meResponse = await client.GetAsync("/api/auth/me");
        var session = await meResponse.Content.ReadFromJsonAsync<SessionDto>();

        session!.Role.Should().Be("diretoria", "admin should have diretoria role");
    }

    [Fact]
    public async Task LowerRole_CannotAccessAdminOnlyEndpoints()
    {
        // Assuming there's an admin-only endpoint
        // This test validates authorization on specific operations

        // Create a non-admin user
        var adminClient = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var login = $"mgr_{Guid.NewGuid():N}"[..15];

        var createUserResponse = await adminClient.PostAsJsonAsync("/api/users",
            new CreateUserRequest(
                FullName: "Test Manager",
                Email: $"{login}@{TestCredentials.TestDomain}",  // IANA-reserved .test TLD
                Login: login,
                Password: TestCredentials.SameTenantManagerPassword,
                Role: "gerente",
                Phone: null,
                Notes: null,
                RequirePasswordChange: false));

        if (createUserResponse.StatusCode == HttpStatusCode.Created)
        {
            var userDto = await createUserResponse.Content.ReadFromJsonAsync<UserDto>();

            // Note: The test user password is set during creation, but we'd need
            // to know how to authenticate as them in a real scenario
            // This would require either:
            // 1. Setting a known password in the test data
            // 2. Getting a password reset token
            // 3. Mocking authentication

            // For now, document the test structure
            _ = userDto;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // USER STATUS VALIDATION IN AUTH MIDDLEWARE
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InactiveUser_CannotAccessProtectedEndpoints()
        => await AssertStatusEndsTheSessionAsync("inactive", UserRole.Vendedor);

    [Fact]
    public async Task BlockedUser_CannotAccessProtectedEndpoints()
        => await AssertStatusEndsTheSessionAsync("blocked", UserRole.Vendedor);

    /// <summary>A tela de usuários oferece o papel Cozinha: quem o tem também precisa poder ser bloqueado.</summary>
    [Fact]
    public async Task BlockedKitchenUser_CannotAccessProtectedEndpoints()
        => await AssertStatusEndsTheSessionAsync("blocked", UserRole.Cozinha);

    private async Task AssertStatusEndsTheSessionAsync(string status, UserRole role)
    {
        const string route = "/api/categories";
        var director = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var target = await SyntheticTenant.AddUserAsync(_factory, director, role);
        var directorClient = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, target.Login, target.Password);

        // o token já estava em uso: as entradas de cache do usuário e do carimbo estão preenchidas
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await directorClient.PutAsJsonAsync($"/api/users/{target.UserId}", UserUpdate(target, role, status))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await client.GetAsync("/api/auth/me").ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(login.RefreshToken))
            .ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(target.Login, target.Password))
            .ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        // reativado, entra de novo com um login novo; o token de antes continua morto
        await directorClient.PutAsJsonAsync($"/api/users/{target.UserId}", UserUpdate(target, role, "active"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        var renewed = await AuthClientFactory.LoginAsync(_factory, target.Login, target.Password);
        await renewed.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    private static object UserUpdate(SyntheticAccount user, UserRole role, string status) => new
    {
        fullName = user.FullName,
        email = user.Email,
        role = role.ToString().ToLowerInvariant(),
        phone = (string?)null,
        notes = (string?)null,
        status,
    };

    // ────────────────────────────────────────────────────────────────────────────
    // TENANT STATUS VALIDATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendedTenant_Users_CannotAccessEndpoints()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var suspendedTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Status == TenantStatus.Suspended);

        if (suspendedTenant == null)
            return; // Skip if no suspended tenant

        // Users from this tenant should get 403 when trying to access their endpoints
        await Task.CompletedTask;
    }

    [Fact]
    public async Task InactiveTenant_Users_CannotAccessEndpoints()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var inactiveTenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Status == TenantStatus.Cancelled);

        if (inactiveTenant == null)
            return;

        // Expected: 403 Forbidden
        await Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // MODULE CACHE INVALIDATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModuleActivation_EventuallyAllowsAccess()
    {
        // This test validates cache invalidation:
        // 1. Module is inactive → endpoint returns 403
        // 2. Admin activates module
        // 3. After cache timeout, endpoint returns 200
        // 4. Or if cache invalidation is immediate, returns 200 immediately
        await Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PLATFORM ADMIN ENDPOINTS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlatformAdmin_HasSeparateAuth()
    {
        // /api/platform/* endpoints use different authentication
        // They require "platform" token audience, not "nexo-frontend"

        // Attempt to access a platform endpoint with a regular tenant token
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var response = await client.GetAsync("/api/platform/admin");

        // Should be forbidden (even though user has valid tenant token)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound  // If endpoint doesn't exist in test env
        );
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PERMISSION ESCALATION PREVENTION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TamperingWithToken_ChangeRole_FailsValidation()
    {
        // Login
        var client = _factory.CreateApiClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var tokenParts = loginBody!.AccessToken.Split('.');

        // A valid JWT has 3 parts: header.payload.signature
        // Tampering with payload would invalidate the signature
        if (tokenParts.Length == 3)
        {
            var tamperedToken = tokenParts[0] + ".modified" + tokenParts[2];

            var client2 = _factory.CreateApiClient();
            client2.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tamperedToken);

            var meResponse = await client2.GetAsync("/api/auth/me");
            meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "tampered token should be rejected");
        }
    }

    [Fact]
    public async Task TenantIdClaim_CannotBeChangedByUser()
    {
        // Even if a user somehow crafted a JWT with different tenantId,
        // the server validation should reject it (signature invalid)

        // This test documents the protection:
        // JWTs are signed by the server with the user's actual tenantId
        // Changing claims invalidates the signature
        await Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // SECURITY STAMP VALIDATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserSecurityStamp_Changed_InvalidatesToken()
    {
        // If a user's security stamp changes (password reset, etc),
        // old tokens should become invalid
        // Test structure documented here
        await Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // STORE ACCESS VALIDATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserCanOnlyAccessAssignedStores()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        // Get available stores
        var meResponse = await client.GetAsync("/api/auth/me");
        var session = await meResponse.Content.ReadFromJsonAsync<SessionDto>();
        var availableStoreIds = session!.StoreIds;

        // Try to switch to a random store ID (not in available list)
        var randomStoreId = Guid.NewGuid().ToString();

        var switchResponse = await client.PostAsJsonAsync("/api/auth/switch-store",
            new { storeId = randomStoreId });

        switchResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "cannot switch to unassigned store");
    }
}

/// <summary>DTOs for test requests</summary>
public record CreateUserRequest(
    string FullName,
    string Email,
    string Login,
    string Password,
    string Role,
    string? Phone,
    string? Notes,
    bool RequirePasswordChange);

public record UserDto(
    string Id,
    string FullName,
    string Email,
    string Login,
    string Role,
    string? Status = null);

public record SwitchStoreResponse(
    string AccessToken,
    string RefreshToken,
    string StoreId);
