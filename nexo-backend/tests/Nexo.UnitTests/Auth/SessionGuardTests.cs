using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Api.Middleware;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Persistence;
using NSubstitute;

namespace Nexo.UnitTests.Auth;

/// <summary>
/// The checks that stand between a credential and a session when the credential itself looks fine: a locked account, a refresh
/// token that does not match its user, a session ended after the token was issued. Each one must refuse and must issue nothing.
/// These rules live in the services, whatever way the token reaches them.
/// </summary>
public class SessionGuardTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IStoreRepository _stores = Substitute.For<IStoreRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();
    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly IMfaChallengeService _mfa = Substitute.For<IMfaChallengeService>();

    private LoginAttemptGuard Attempts => new(_cache, NullLogger<LoginAttemptGuard>.Instance);

    private AuthService Sut() => new(_users, _tenants, _stores, _hasher, _jwt, _cache, _sessions, _currentTenant, null, Attempts, _mfa);

    private static User Person(Guid tenantId, UserRole role = UserRole.Vendedor) => User.Create(
        tenantId: tenantId, fullName: "Pessoa", email: "pessoa@teste.dev", login: "pessoa", passwordHash: "stored-hash", role: role);

    private void IsLocked(string account) => _cache.ExistsAsync($"login:locked:{account}", Arg.Any<CancellationToken>()).Returns(true);

    private void NothingWasIssued()
        => _jwt.DidNotReceiveWithAnyArgs().GenerateTokenPair(default!, default!, default!, default!, default, default!);

    // ── Second step of the sign-in ────────────────────────────────────────────

    [Fact]
    public async Task A_locked_account_is_refused_at_the_second_step_before_the_code_is_even_looked_at()
    {
        var user = Person(Guid.NewGuid());
        user.EnableMfa("protected-secret", 1, ["hash"]);
        _mfa.ResolveChallengeAsync("challenge", Arg.Any<CancellationToken>()).Returns(user.Id);
        _users.GetByIdAcrossTenantsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        IsLocked(user.Id.ToString());

        var outcome = await Sut().VerifyMfaAsync(new MfaVerifyRequest("challenge", "123456"));

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("account_locked");
        await _mfa.DidNotReceiveWithAnyArgs().CheckCodeAsync(default!, default, default, default);
        NothingWasIssued();
    }

    // ── Renewal ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_refresh_token_whose_company_is_not_the_one_of_its_user_is_refused_and_renews_nothing()
    {
        var user = Person(Guid.NewGuid());
        var otherCompany = Guid.NewGuid();
        var claims = new RefreshTokenClaims("jti", user.Id, otherCompany);
        _jwt.ValidateRefreshToken("token").Returns(claims);
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti", Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenEntry(user.Id, otherCompany));
        _users.GetByIdAcrossTenantsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var outcome = await Sut().RefreshAsync(new RefreshTokenRequest("token"));

        outcome.Tokens.Should().BeNull();
        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        NothingWasIssued();
    }

    [Fact]
    public async Task A_refresh_token_stored_for_someone_else_is_refused_and_renews_nothing()
    {
        var user = Person(Guid.NewGuid());
        _jwt.ValidateRefreshToken("token").Returns(new RefreshTokenClaims("jti", user.Id, user.TenantId));
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti", Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenEntry(Guid.NewGuid(), user.TenantId));
        _users.GetByIdAcrossTenantsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var outcome = await Sut().RefreshAsync(new RefreshTokenRequest("token"));

        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        NothingWasIssued();
    }

    // ── Switching store renews the session too ────────────────────────────────

    private (User User, Store Store) CompanyWithStore()
    {
        var tenantId = Guid.NewGuid();
        var user = Person(tenantId);
        var store = Store.Create(tenantId, "Loja", "loja");
        _stores.GetByIdAsync(store.Id, Arg.Any<CancellationToken>()).Returns(store);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Tenant.Create("Empresa", "00.000.000/0001-00", "a@b.dev"));
        _jwt.ValidateRefreshToken("token").Returns(new RefreshTokenClaims("jti", user.Id, tenantId, "family"));
        return (user, store);
    }

    [Fact]
    public async Task A_person_who_is_no_longer_active_cannot_switch_store()
    {
        var (user, store) = CompanyWithStore();
        user.SetStatus(UserStatus.Inactive);

        var outcome = await Sut().SwitchStoreAsync(user.Id, user.TenantId, store.Id, "token");

        outcome.ErrorCode.Should().Be(SwitchStoreErrors.StoreForbidden);
        NothingWasIssued();
    }

    [Fact]
    public async Task Switching_store_with_a_token_stored_for_someone_else_is_refused()
    {
        var (user, store) = CompanyWithStore();
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti", Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenEntry(Guid.NewGuid(), user.TenantId, null, user.SecurityStamp));

        var outcome = await Sut().SwitchStoreAsync(user.Id, user.TenantId, store.Id, "token");

        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        NothingWasIssued();
    }

    [Fact]
    public async Task Switching_store_after_the_sessions_were_ended_is_refused()
    {
        var (user, store) = CompanyWithStore();
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti", Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenEntry(user.Id, user.TenantId, null, "stamp-from-before-the-change"));

        var outcome = await Sut().SwitchStoreAsync(user.Id, user.TenantId, store.Id, "token");

        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        NothingWasIssued();
    }

    [Fact]
    public async Task Switching_store_in_a_session_whose_family_was_ended_is_refused()
    {
        var (user, store) = CompanyWithStore();
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti", Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenEntry(user.Id, user.TenantId, null, user.SecurityStamp));
        _cache.ExistsAsync("refresh:family:family:revoked", Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await Sut().SwitchStoreAsync(user.Id, user.TenantId, store.Id, "token");

        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        NothingWasIssued();
    }

    // ── Manager authorization at the till ─────────────────────────────────────

    [Fact]
    public async Task A_locked_manager_authorizes_nothing_even_with_the_right_password()
    {
        var tenantId = Guid.NewGuid();
        var manager = Person(tenantId, UserRole.Gerente);
        _currentTenant.Id.Returns(tenantId);
        _users.GetByLoginInTenantAsync("pessoa", tenantId, Arg.Any<CancellationToken>()).Returns(manager);
        _hasher.Verify("right", manager.PasswordHash).Returns(true);
        IsLocked(manager.Id.ToString());

        var result = await Sut().VerifyManagerAsync(new VerifyManagerRequest("pessoa", "right"));

        result.Authorized.Should().BeFalse();
        _hasher.DidNotReceiveWithAnyArgs().Verify(default!, default!);
    }

    // ── Two-step verification under the same lockout ──────────────────────────

    [Fact]
    public async Task A_locked_account_cannot_try_a_password_in_the_two_step_settings()
    {
        var user = Person(Guid.NewGuid());
        _users.GetByIdAcrossTenantsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        IsLocked(user.Id.ToString());
        var mfa = new MfaService(_users, _hasher, Substitute.For<IMfaSecretProtector>(), _cache, Substitute.For<IAuditWriter>(), Attempts);

        var act = () => mfa.StartSetupAsync(user.Id, "any-password");

        await act.Should().ThrowAsync<ForbiddenException>();
        _hasher.DidNotReceiveWithAnyArgs().Verify(default!, default!);
        user.MfaPendingSecret.Should().BeNull();
    }

    // ── The directors lock ────────────────────────────────────────────────────

    [Fact]
    public async Task The_directors_lock_refuses_to_run_outside_a_transaction_where_it_would_protect_nothing()
    {
        var options = new DbContextOptionsBuilder<NexoDbContext>().UseNpgsql("Host=never-connected").Options;
        await using var db = new NexoDbContext(options, Substitute.For<ICurrentTenant>(), Substitute.For<ICurrentStore>());

        var act = () => LastDirectorGuard.LockAsync(db, Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── A token whose user cannot be read ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task A_token_with_no_valid_user_claim_is_refused_before_anything_runs(string? userIdClaim)
    {
        var claims = new List<Claim> { new("security_stamp", "stamp") };
        if (userIdClaim is not null) claims.Add(new Claim("userId", userIdClaim));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
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
}
