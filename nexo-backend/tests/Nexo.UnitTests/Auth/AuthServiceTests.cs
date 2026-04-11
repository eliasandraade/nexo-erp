using FluentAssertions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using NSubstitute;

namespace Nexo.UnitTests.Auth;

public class AuthServiceTests
{
    private readonly IUserRepository    _users   = Substitute.For<IUserRepository>();
    private readonly ITenantRepository  _tenants = Substitute.For<ITenantRepository>();
    private readonly IStoreRepository   _stores  = Substitute.For<IStoreRepository>();
    private readonly IPasswordHasher    _hasher  = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenService   _jwt     = Substitute.For<IJwtTokenService>();
    private readonly ICacheService      _cache   = Substitute.For<ICacheService>();

    private AuthService CreateSut() => new(_users, _tenants, _stores, _hasher, _jwt, _cache);

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsLoginResponse()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var user = BuildUser(UserRole.Vendedor, tenantId: tenantId);
        var tenant = Tenant.Create("Loja", "00.000.000/0001-00", "a@b.com", null, null, "varejo");

        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("senha123", user.PasswordHash).Returns(true);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenants.GetActiveModuleKeysAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "varejo" });
        _stores.GetByTenantIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<Store>());
        _jwt.GenerateTokenPair(
            Arg.Any<User>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<Guid>>())
            .Returns(new TokenPair("fake-access", "fake-refresh",
                DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow.AddDays(7)));
        _jwt.ValidateRefreshToken("fake-refresh").Returns((RefreshTokenClaims?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.LoginAsync(new LoginRequest("joao", "senha123"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Response!.AccessToken.Should().Be("fake-access");
        result.Response.Session.Login.Should().Be("joao");
        result.Response.Session.Role.Should().Be("vendedor");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsFail()
    {
        var user = BuildUser(UserRole.Vendedor);
        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong", user.PasswordHash).Returns(false);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("joao", "wrong"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentLogin_ReturnsFail()
    {
        _users.GetByLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("ghost", "pass"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task LoginAsync_WithBlockedUser_ReturnsFail()
    {
        var user = BuildUser(UserRole.Vendedor, UserStatus.Blocked);
        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("joao", "senha123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("account_blocked");
    }

    [Fact]
    public async Task LoginAsync_WithPendingVerification_ReturnsFail()
    {
        var user = BuildUser(UserRole.Vendedor, UserStatus.PendingVerification);
        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("joao", "senha123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("email_not_verified");
    }

    // ── VerifyManagerAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyManagerAsync_WithValidManager_ReturnsAuthorized()
    {
        var manager = BuildUser(UserRole.Gerente);
        _users.GetByLoginAsync("gerente", Arg.Any<CancellationToken>()).Returns(manager);
        _hasher.Verify("senha123", manager.PasswordHash).Returns(true);

        var sut = CreateSut();
        var result = await sut.VerifyManagerAsync(new VerifyManagerRequest("gerente", "senha123"));

        result.Authorized.Should().BeTrue();
        result.Role.Should().Be("gerente");
        result.ManagerName.Should().Be(manager.FullName);
    }

    [Fact]
    public async Task VerifyManagerAsync_WithVendedor_ReturnsNotAuthorized()
    {
        var vendedor = BuildUser(UserRole.Vendedor);
        _users.GetByLoginAsync("vendedor", Arg.Any<CancellationToken>()).Returns(vendedor);
        _hasher.Verify("senha123", vendedor.PasswordHash).Returns(true);

        var sut = CreateSut();
        var result = await sut.VerifyManagerAsync(new VerifyManagerRequest("vendedor", "senha123"));

        result.Authorized.Should().BeFalse();
        result.ManagerUserId.Should().BeNull();
    }

    [Fact]
    public async Task VerifyManagerAsync_WithWrongPassword_ReturnsNotAuthorized()
    {
        var manager = BuildUser(UserRole.Gerente);
        _users.GetByLoginAsync("gerente", Arg.Any<CancellationToken>()).Returns(manager);
        _hasher.Verify("wrong", manager.PasswordHash).Returns(false);

        var sut = CreateSut();
        var result = await sut.VerifyManagerAsync(new VerifyManagerRequest("gerente", "wrong"));

        result.Authorized.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static User BuildUser(
        UserRole role,
        UserStatus status = UserStatus.Active,
        Guid? tenantId = null,
        string login = "joao")
    {
        var user = User.Create(
            tenantId:     tenantId ?? Guid.NewGuid(),
            fullName:     "João Silva",
            email:        $"{login}@nexo.local",
            login:        login,
            passwordHash: "$2a$12$hash",
            role:         role);

        if (status != UserStatus.Active)
            user.SetStatus(status);

        return user;
    }
}
