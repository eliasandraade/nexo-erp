using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Auth;
using NSubstitute;

namespace Nexo.UnitTests.Auth;

/// <summary>
/// The paths of the renewal policy that a real Redis cannot produce on demand: the cache failing between two calls of the same
/// renewal, and the exact way a token is told apart. The whole flow, with a real Redis: RefreshRateLimitingRedisTests.
/// </summary>
public class RefreshPolicyTests
{
    private const string Token = "presented-token";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IStoreRepository _stores = Substitute.For<IStoreRepository>();
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();

    private AuthService Sut(RefreshBudgetOptions? options = null) => new(
        _users, _tenants, _stores, Substitute.For<IPasswordHasher>(), _jwt, _cache, _sessions, Substitute.For<ICurrentTenant>(),
        budget: new RefreshBudget(_cache, options ?? new RefreshBudgetOptions { FailureBudget = 1, FamilyBudget = 1 }, NullLogger<RefreshBudget>.Instance));

    private static readonly RefreshTokenClaims Claims = new("jti-1", UserId, TenantId, "family-1");

    private void TokenIsValidWithAnEntry()
    {
        _jwt.ValidateRefreshToken(Token).Returns(Claims);
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti-1", Arg.Any<CancellationToken>()).Returns(new RefreshTokenEntry(UserId, TenantId));
        var user = User.Create(tenantId: TenantId, fullName: "Pessoa", email: "a@nexo.test", login: "a", passwordHash: "x", role: UserRole.Vendedor);
        user.SetStatus(UserStatus.Active);
        _users.GetByIdAcrossTenantsAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant.Create("Loja", "00000000000100", "a@nexo.test"));
        _tenants.GetActiveModuleKeysAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new List<string>());
        _stores.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new List<Store>());
    }

    [Fact]
    public async Task Cache_failing_right_before_the_claim_is_no_race_it_asks_to_come_back()
    {
        TokenIsValidWithAnEntry();
        _cache.TryClaimAsync(Arg.Any<CacheClaim>(), Arg.Any<CancellationToken>()).Returns((bool?)null);

        var outcome = await Sut().RefreshAsync(new RefreshTokenRequest(Token), "203.0.113.1");

        outcome.ErrorCode.Should().Be(RefreshErrors.Unavailable, "a 409 here would lead the client to end the session after its wait");
        outcome.RetryAfter.Should().NotBeNull();
        _jwt.DidNotReceiveWithAnyArgs().GenerateTokenPair(default!, default!, default!, default!, default, default!, default);
    }

    [Fact]
    public async Task Cache_failing_during_the_reuse_check_is_no_verdict()
    {
        _jwt.ValidateRefreshToken(Token).Returns(Claims);
        _cache.GetAsync<RefreshTokenEntry>("refresh:valid:jti-1", Arg.Any<CancellationToken>()).Returns((RefreshTokenEntry?)null);
        // the entry really had no value, and the cache went away on the next read: the head of the family comes back empty
        _cache.GetAsync<RefreshFamilyHead>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _cache.IsUnavailable.Returns(true);
            return (RefreshFamilyHead?)null;
        });
        _sessions.IsActiveAsync("jti-1", Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await Sut().RefreshAsync(new RefreshTokenRequest(Token), "203.0.113.1");

        outcome.ErrorCode.Should().Be(RefreshErrors.Unavailable);
    }

    [Fact]
    public async Task Without_the_cache_a_session_the_table_knows_as_ended_gets_the_verdict()
    {
        _jwt.ValidateRefreshToken(Token).Returns(Claims);
        _cache.IsUnavailable.Returns(true);
        _sessions.IsActiveAsync("jti-1", Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Sut().RefreshAsync(new RefreshTokenRequest(Token), "203.0.113.1");

        outcome.ErrorCode.Should().Be(RefreshErrors.SessionEnded);
        outcome.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task A_budget_that_stopped_counting_lets_the_renewal_go_on()
    {
        _jwt.ValidateRefreshToken(Token).Returns((RefreshTokenClaims?)null);
        _cache.CountInWindowAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns((WindowCount?)null);

        var outcome = await Sut().RefreshAsync(new RefreshTokenRequest(Token), "203.0.113.1");

        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        outcome.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task An_expired_token_of_ours_counts_nothing_and_a_foreign_one_does()
    {
        _jwt.ValidateRefreshToken(Arg.Any<string>()).Returns((RefreshTokenClaims?)null);
        _jwt.IsRefreshTokenOurs("expired").Returns(true);
        _jwt.IsRefreshTokenOurs("forged").Returns(false);
        _cache.CountInWindowAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(new WindowCount(2, TimeSpan.FromMinutes(1)));

        (await Sut().RefreshAsync(new RefreshTokenRequest("expired"), "203.0.113.1")).ErrorCode.Should().Be(RefreshErrors.Invalid);
        await _cache.DidNotReceiveWithAnyArgs().CountInWindowAsync(default!, default);

        var forged = await Sut().RefreshAsync(new RefreshTokenRequest("forged"), "203.0.113.1");
        forged.ErrorCode.Should().Be(RefreshErrors.OverBudget);
        forged.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task With_the_policy_off_nothing_is_counted()
    {
        _jwt.ValidateRefreshToken(Token).Returns((RefreshTokenClaims?)null);

        var outcome = await Sut(new RefreshBudgetOptions { Enabled = false, FailureBudget = 0 }).RefreshAsync(new RefreshTokenRequest(Token), "203.0.113.1");

        outcome.ErrorCode.Should().Be(RefreshErrors.Invalid);
        await _cache.DidNotReceiveWithAnyArgs().CountInWindowAsync(default!, default);
    }

    [Theory]
    [InlineData("203.0.113.9", "refresh:budget:ip:203.0.113.9")]
    [InlineData("2001:db8::1", "refresh:budget:ip:2001:db8::1")]
    public void An_address_is_the_key_as_it_is(string address, string key)
        => RefreshBudget.FailureKey(address).Should().Be(key);

    [Fact]
    public void Anything_else_in_the_header_has_a_bounded_key()
    {
        var key = RefreshBudget.FailureKey(new string('x', 100_000));

        key.Length.Should().BeLessThan(100);
        RefreshBudget.FailureKey("unknown").Should().NotContain("unknown");
    }

    // ── Telling a token apart ──────────────────────────────────────────────────

    private const string Issuer = "nexo-api";
    private const string Audience = "nexo-refresh";
    private static readonly string OurKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static readonly JwtTokenService Jwt = new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = OurKey, ["Jwt:Issuer"] = Issuer, ["Jwt:RefreshAudience"] = Audience })
        .Build());

    private static string Sign(string key, TimeSpan lifetimeFromNow, string issuer = Issuer, string audience = Audience, string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var expires = DateTime.UtcNow + lifetimeFromNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
            [new Claim(JwtRegisteredClaimNames.Jti, "j"), new Claim("userId", UserId.ToString()), new Claim("tenantId", TenantId.ToString())],
            notBefore: expires.AddDays(-30), expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm)));
    }

    private static readonly TimeSpan Past = TimeSpan.FromDays(-3);
    private static readonly TimeSpan Future = TimeSpan.FromDays(3);

    [Fact]
    public void An_expired_token_of_ours_is_ours()
    {
        var token = Sign(OurKey, Past);

        Jwt.ValidateRefreshToken(token).Should().BeNull("it no longer renews anything");
        Jwt.IsRefreshTokenOurs(token).Should().BeTrue();
    }

    [Fact]
    public void A_token_in_date_is_ours_too()
    {
        var token = Sign(OurKey, Future);

        Jwt.ValidateRefreshToken(token).Should().NotBeNull();
        Jwt.IsRefreshTokenOurs(token).Should().BeTrue();
    }

    /// <summary>The way around the budget, if an old date alone were enough: a guess with a past expiry passing for a terminal back from the weekend.</summary>
    [Fact]
    public void A_made_up_token_with_an_old_date_is_not_ours()
        => Jwt.IsRefreshTokenOurs(Sign(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), Past)).Should().BeFalse();

    [Fact]
    public void A_token_of_another_issuer_or_audience_is_not_ours()
    {
        Jwt.IsRefreshTokenOurs(Sign(OurKey, Past, issuer: "another")).Should().BeFalse();
        Jwt.IsRefreshTokenOurs(Sign(OurKey, Past, audience: "nexo-frontend")).Should().BeFalse("an access token is not a refresh token");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public void Anything_malformed_is_not_ours(string token)
        => Jwt.IsRefreshTokenOurs(token).Should().BeFalse();

    [Fact]
    public void An_unsigned_token_is_not_ours()
    {
        var unsigned = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Audience,
            [new Claim(JwtRegisteredClaimNames.Jti, "j")], expires: DateTime.UtcNow.AddDays(-1), notBefore: DateTime.UtcNow.AddDays(-2)));

        Jwt.IsRefreshTokenOurs(unsigned).Should().BeFalse();
    }
}
