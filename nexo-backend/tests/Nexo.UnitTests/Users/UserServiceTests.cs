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
    private readonly IUserRepository _users   = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher  = Substitute.For<IPasswordHasher>();
    private readonly IAuditWriter    _audit   = Substitute.For<IAuditWriter>();
    private readonly ICurrentUser    _current = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant  _tenant  = Substitute.For<ICurrentTenant>();
    private readonly IUnitOfWork     _uow     = Substitute.For<IUnitOfWork>();
    private readonly IUserSessionRevoker _sessions = Substitute.For<IUserSessionRevoker>();

    private UserService CreateSut() => new(_users, _hasher, _audit, _current, _tenant, _uow, _sessions);

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidRequest_ReturnsNewUser()
    {
        _users.LoginExistsAsync("joao.silva", Arg.Any<CancellationToken>()).Returns(false);
        _users.EmailExistsAsync("joao@loja.com", Arg.Any<CancellationToken>()).Returns(false);
        _hasher.Hash("senha123").Returns("hashed");
        _current.IsAuthenticated.Returns(false);
        _tenant.Id.Returns(Guid.NewGuid());

        var sut = CreateSut();
        var result = await sut.CreateAsync(new CreateUserRequest(
            FullName: "João Silva",
            Email:    "joao@loja.com",
            Login:    "joao.silva",
            Password: "senha123",
            Role:     "vendedor",
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
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "A", "a@b.com", "a", "hash", UserRole.Vendedor);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);
        _tenant.Id.Returns(tenantId);

        var sut = CreateSut();
        var act = () => sut.ChangePasswordAsync(user.Id,
            new ChangePasswordRequest("wrong", "newpass"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ── UpdateAsync: the last active Diretoria ────────────────────────────────

    private static UpdateUserRequest EditOf(User user, string role, string? status) =>
        new(user.FullName, user.Email, role, user.Phone, user.Notes, status);

    private User ExistingDirector()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "A", "a@b.com", "a", "hash", UserRole.Diretoria);
        _tenant.Id.Returns(tenantId);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    [InlineData("diretoria", "inactive")]
    public async Task UpdateAsync_RefusedForTheLastDirector_TouchesNothing(string role, string status)
    {
        var user = ExistingDirector();
        var stamp = user.SecurityStamp;
        _users.HasAnotherActiveDirectorAsync(user.TenantId, user.Id, Arg.Any<CancellationToken>()).Returns(false);

        var act = () => CreateSut().UpdateAsync(user.Id, EditOf(user, role, status));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(LastDirectorException.ErrorCode);
        user.Role.Should().Be(UserRole.Diretoria);
        user.Status.Should().Be(UserStatus.Active);
        user.SecurityStamp.Should().Be(stamp);
        await _sessions.DidNotReceiveWithAnyArgs().RevokeAllAsync(default);
        await _users.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task UpdateAsync_TakesTheLockBeforeReadingAnything()
    {
        var user = ExistingDirector();
        _users.HasAnotherActiveDirectorAsync(user.TenantId, user.Id, Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().UpdateAsync(user.Id, EditOf(user, "gerente", "active"));

        Received.InOrder(() =>
        {
            _uow.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _users.LockDirectorsAsync(user.TenantId, Arg.Any<CancellationToken>());
            _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>());
            _users.HasAnotherActiveDirectorAsync(user.TenantId, user.Id, Arg.Any<CancellationToken>());
            _users.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task UpdateAsync_OfSomeoneWhoStaysADirector_DoesNotAskIfAnotherOneExists()
    {
        var user = ExistingDirector();

        await CreateSut().UpdateAsync(user.Id, EditOf(user, "Diretoria", null));

        await _users.DidNotReceiveWithAnyArgs().HasAnotherActiveDirectorAsync(default, default);
        await _sessions.DidNotReceiveWithAnyArgs().RevokeAllAsync(default);
    }
}
