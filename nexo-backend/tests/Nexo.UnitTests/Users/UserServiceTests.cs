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

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("login_taken");
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_AsksForTheLoginAcrossEveryCompany_BeforeAnythingElse()
    {
        _users.LoginExistsAsync("Joao.Silva", Arg.Any<CancellationToken>()).Returns(true);

        var sut = CreateSut();
        var act = () => sut.CreateAsync(new CreateUserRequest(
            FullName: "João Silva",
            Email:    "joao@loja.com",
            Login:    "Joao.Silva",
            Password: "senha123",
            Role:     "vendedor",
            Phone:    null,
            Notes:    null));

        var refused = (await act.Should().ThrowAsync<ConflictException>()).Which;
        refused.Code.Should().Be("login_taken");
        refused.Message.Should().NotContain("Joao", "the answer repeats nothing of what was typed and names no company");
        await _users.DidNotReceive().EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("maria.gerente")]
    [InlineData("MARIA.GERENTE")]
    [InlineData("  Maria.Gerente ")]
    public async Task ValidateManagerAsync_FindsTheManager_InAnySpellingOfTheLogin(string typed)
    {
        var manager = User.Create(Guid.NewGuid(), "Maria Gerente", "maria@loja.com", "maria.gerente", "hashed", UserRole.Gerente);
        _users.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<User> { manager });
        _hasher.Verify("senha123", "hashed").Returns(true);

        var result = await CreateSut().ValidateManagerAsync(new ValidateManagerRequest(typed, "senha123"));

        result.Success.Should().BeTrue();
        result.FullName.Should().Be("Maria Gerente");
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

    // ── UpdateAsync: the status of whoever never proved the address ───────────

    private User ExistingUser(UserStatus status)
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "A", "a@b.com", "a", "hash", UserRole.Vendedor);
        user.SetStatus(status);
        _tenant.Id.Returns(tenantId);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    [Theory]
    [InlineData("pendingverification")]
    [InlineData("PendingVerification")]
    [InlineData(null)]
    public async Task UpdateAsync_OfAPendingUser_KeepsThemPending(string? status)
    {
        var user = ExistingUser(UserStatus.PendingVerification);

        var result = await CreateSut().UpdateAsync(user.Id, EditOf(user, "vendedor", status));

        user.Status.Should().Be(UserStatus.PendingVerification);
        result.Status.Should().Be("pendingverification");
    }

    [Fact]
    public async Task UpdateAsync_RefusesToActivateAPendingUser_AndTouchesNothing()
    {
        var user = ExistingUser(UserStatus.PendingVerification);

        var act = () => CreateSut().UpdateAsync(user.Id, new UpdateUserRequest("Outro Nome", user.Email, "vendedor", null, null, "active"));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("email_not_verified");
        user.Status.Should().Be(UserStatus.PendingVerification);
        user.FullName.Should().Be("A");
        await _users.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Inactive)]
    [InlineData(UserStatus.Blocked)]
    public async Task UpdateAsync_DoesNotSendBackToPendingWhoeverProvedTheAddress(UserStatus current)
    {
        var user = ExistingUser(current);

        var act = () => CreateSut().UpdateAsync(user.Id, EditOf(user, "vendedor", "pendingverification"));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("status_not_editable");
        user.Status.Should().Be(current);
    }

    [Theory]
    [InlineData(UserStatus.Inactive)]
    [InlineData(UserStatus.Blocked)]
    public async Task UpdateAsync_ReactivatingWhoNeverVerified_GoesBackToPending(UserStatus stoppedAs)
    {
        var user = ExistingUser(UserStatus.PendingVerification);
        await CreateSut().UpdateAsync(user.Id, EditOf(user, "vendedor", stoppedAs.ToString()));
        user.Status.Should().Be(stoppedAs);

        var result = await CreateSut().UpdateAsync(user.Id, EditOf(user, "vendedor", "active"));

        user.Status.Should().Be(UserStatus.PendingVerification);
        result.Status.Should().Be("pendingverification");
    }

    [Fact]
    public void The_validator_takes_the_status_the_api_itself_returns()
    {
        var validator = new Nexo.Application.Validators.Users.UpdateUserRequestValidator();

        validator.Validate(new UpdateUserRequest("A", "a@b.com", "vendedor", null, null, "pendingverification")).IsValid.Should().BeTrue();
        validator.Validate(new UpdateUserRequest("A", "a@b.com", "vendedor", null, null, "qualquer")).IsValid.Should().BeFalse();
    }
}
