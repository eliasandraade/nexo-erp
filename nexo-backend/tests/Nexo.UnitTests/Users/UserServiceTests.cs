using FluentAssertions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Users;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using NSubstitute;

namespace Nexo.UnitTests.Users;

public class UserServiceTests
{
    private readonly IUserRepository _users  = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IAuditWriter    _audit  = Substitute.For<IAuditWriter>();
    private readonly ICurrentUser    _current = Substitute.For<ICurrentUser>();

    private UserService CreateSut() => new(_users, _hasher, _audit, _current);

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidRequest_ReturnsNewUser()
    {
        _users.LoginExistsAsync("joao.silva", Arg.Any<CancellationToken>()).Returns(false);
        _users.EmailExistsAsync("joao@loja.com", Arg.Any<CancellationToken>()).Returns(false);
        _hasher.Hash("senha123").Returns("hashed");
        _current.IsAuthenticated.Returns(false);

        var sut = CreateSut();
        var result = await sut.CreateAsync(new CreateUserRequest(
            FullName: "João Silva",
            Email:    "joao@loja.com",
            Login:    "joao.silva",
            Password: "senha123",
            Role:     "vendedor",
            StoreId:  null,
            Phone:    null,
            Notes:    null));

        result.Should().NotBeNull();
        result.Login.Should().Be("joao.silva");
        result.Role.Should().Be("vendedor");
        result.Status.Should().Be("active");
        await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateLogin_ThrowsConflict()
    {
        _users.LoginExistsAsync("joao.silva", Arg.Any<CancellationToken>()).Returns(true);

        var sut = CreateSut();
        var act = () => sut.CreateAsync(new CreateUserRequest(
            FullName: "João Silva",
            Email:    "joao@loja.com",
            Login:    "joao.silva",
            Password: "senha123",
            Role:     "vendedor",
            StoreId:  null,
            Phone:    null,
            Notes:    null));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateEmail_ThrowsConflict()
    {
        _users.LoginExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _users.EmailExistsAsync("joao@loja.com", Arg.Any<CancellationToken>()).Returns(true);

        var sut = CreateSut();
        var act = () => sut.CreateAsync(new CreateUserRequest(
            FullName: "João Silva",
            Email:    "joao@loja.com",
            Login:    "joao.silva",
            Password: "senha123",
            Role:     "vendedor",
            StoreId:  null,
            Phone:    null,
            Notes:    null));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateAsync_WithInvalidRole_ThrowsDomainException()
    {
        _users.LoginExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var sut = CreateSut();
        var act = () => sut.CreateAsync(new CreateUserRequest(
            FullName: "João",
            Email:    "j@loja.com",
            Login:    "j",
            Password: "senha",
            Role:     "superadmin",   // invalid
            StoreId:  null,
            Phone:    null,
            Notes:    null));

        await act.Should().ThrowAsync<DomainException>();
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ThrowsNotFoundException()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var sut = CreateSut();
        var act = () => sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── ChangePasswordAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_ThrowsForbidden()
    {
        var user = User.Create("A", "a@b.com", "a", "hash", UserRole.Vendedor);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);

        var sut = CreateSut();
        var act = () => sut.ChangePasswordAsync(user.Id,
            new ChangePasswordRequest("wrong", "newpass"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
