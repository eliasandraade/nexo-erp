using FluentAssertions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using NSubstitute;

namespace Nexo.UnitTests.Auth;

public class AuthServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenService _jwt   = Substitute.For<IJwtTokenService>();

    private AuthService CreateSut() => new(_users, _hasher, _jwt);

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsLoginResponse()
    {
        // Arrange
        var user = BuildUser(UserRole.Vendedor);
        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("senha123", user.PasswordHash).Returns(true);
        _jwt.GenerateToken(user).Returns("fake-jwt");
        _jwt.GetExpiration().Returns(DateTime.UtcNow.AddHours(8));

        var sut = CreateSut();

        // Act
        var result = await sut.LoginAsync(new LoginRequest("joao", "senha123"));

        // Assert
        result.Should().NotBeNull();
        result!.Token.Should().Be("fake-jwt");
        result.Session.Login.Should().Be("joao");
        result.Session.Role.Should().Be("vendedor");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        var user = BuildUser(UserRole.Vendedor);
        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong", user.PasswordHash).Returns(false);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("joao", "wrong"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentLogin_ReturnsNull()
    {
        _users.GetByLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("ghost", "pass"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithBlockedUser_ReturnsNull()
    {
        var user = BuildUser(UserRole.Vendedor, UserStatus.Blocked);
        _users.GetByLoginAsync("joao", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("joao", "senha123"));

        result.Should().BeNull();
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

    private static User BuildUser(UserRole role, UserStatus status = UserStatus.Active)
        => User.Create(
            fullName:     "João Silva",
            email:        $"{role.ToString().ToLower()}@nexo.local",
            login:        role.ToString().ToLower(),
            passwordHash: "$2a$12$hash",
            role:         role);
}
