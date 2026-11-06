using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Auth.BrowserSessionKit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// The refresh token in the cookie <c>__Host-nexo_rt</c>, per request, on <c>X-Orken-Session: cookie</c>
/// (docs/security/sessao-auditoria-e-migracao.md, section 3). Every path that issues or ends a session, the three barriers
/// against CSRF, and what stays exactly as it was for a request without the header.
/// </summary>
[Collection("Integration")]
public class SessionCookieModeTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly BrowserSessionKit _kit;

    public SessionCookieModeTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _kit = new BrowserSessionKit(factory);
    }

    /// <summary>Every Origin that is not exactly one of the list. A sibling subdomain is same-site, so SameSite says nothing about it.</summary>
    public static TheoryData<string?> RefusedOrigins => new()
    {
        "https://evil.example",
        "https://evil.orken.com.br",
        "https://erp.orken.com.br.evil.example",
        "http://erp.orken.com.br",
        "http://localhost:5173",
        "null",
        null,
    };

    // ── Every path that issues a pair ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_sets_the_cookie_and_keeps_the_refresh_token_out_of_the_body()
    {
        var account = await _kit.NewAccountAsync();

        var response = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetTheSessionCookie(response, TokenExpiry(CookieValue(response)));
        await ShouldNotCarryARefreshTokenAsync(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("session").GetProperty("login").GetString().Should().Be(account.Login);
    }

    [Fact]
    public async Task Refresh_through_the_cookie_rotates_it()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var response = await _kit.RenewAsync(session.Cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CookieValue(response).Should().NotBe(session.Cookie);
        ShouldSetTheSessionCookie(response, TokenExpiry(CookieValue(response)));
        await ShouldNotCarryARefreshTokenAsync(response);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString().Should().NotBe(session.Access);
    }

    [Fact]
    public async Task Refresh_accepts_an_empty_body_and_an_empty_json_object()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var bodyless = await _kit.RenewAsync(session.Cookie);
        bodyless.StatusCode.Should().Be(HttpStatusCode.OK);

        var emptyObject = await _kit.PostAsync("/api/auth/refresh", new { refreshToken = "" }, sessionCookie: CookieValue(bodyless));
        emptyObject.StatusCode.Should().Be(HttpStatusCode.OK, await emptyObject.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_without_any_token_in_the_cookie_mode_is_a_401_the_client_can_read_as_no_session()
    {
        var response = await _kit.PostAsync("/api/auth/refresh");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldSetNoCookie(response);
    }

    [Fact]
    public async Task Switch_store_through_the_cookie_moves_the_session_and_the_store_survives_the_renewal()
    {
        var account = await _kit.NewAccountAsync();
        var second = await AddStoreAsync(account);
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var switched = await _kit.PostAsync("/api/auth/switch-store", new { storeId = second }, sessionCookie: session.Cookie, bearer: session.Access);

        switched.StatusCode.Should().Be(HttpStatusCode.OK, await switched.Content.ReadAsStringAsync());
        ShouldSetTheSessionCookie(switched, TokenExpiry(CookieValue(switched)));
        await ShouldNotCarryARefreshTokenAsync(switched);
        var body = await switched.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("session").GetProperty("storeId").GetString().Should().Be(second.ToString());

        var renewed = await _kit.RenewAsync(CookieValue(switched));
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = (await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        ClaimOf(access, "storeId").Should().Be(second.ToString(), "the new store lives with the refresh token");
    }

    [Fact]
    public async Task Two_step_verification_sets_the_cookie_only_on_the_second_step()
    {
        var account = await _kit.NewAccountAsync();
        var (secret, _) = await EnableTwoStepAsync(account);

        var first = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(first);
        var challenge = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaChallenge").GetString();

        var verified = await _kit.PostAsync("/api/auth/mfa/verify", new { challenge, code = CodeAt(secret, 1) });

        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        ShouldSetTheSessionCookie(verified, TokenExpiry(CookieValue(verified)));
        await ShouldNotCarryARefreshTokenAsync(verified);
    }

    [Fact]
    public async Task Verify_email_sets_the_cookie()
    {
        var token = await NewPendingEmailLinkAsync();

        var response = await _kit.SendAsync(HttpMethod.Get, $"/api/auth/verify-email?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ShouldSetTheSessionCookie(response, TokenExpiry(CookieValue(response)));
        await ShouldNotCarryARefreshTokenAsync(response);
    }

    [Theory]
    [InlineData("/api/platform/auth/login")]
    [InlineData("/api/auth/login")]
    public async Task A_platform_sign_in_sets_the_cookie_on_either_route(string route)
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();

        var response = await _kit.PostAsync(route, route.Contains("platform") ? new { email, password } : TestCredentials.LoginPayload(email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CookieValue(response).Should().StartWith("p.");
        ShouldSetTheSessionCookie(response, await PlatformExpiryAsync(CookieValue(response)));
        await ShouldNotCarryARefreshTokenAsync(response);
    }

    [Theory]
    [InlineData("/api/platform/auth/refresh")]
    [InlineData("/api/auth/refresh")]
    public async Task A_platform_renewal_through_the_cookie_rotates_it_on_either_route(string route)
    {
        var session = await PlatformSignInAsync();

        var response = await _kit.PostAsync(route, sessionCookie: session.Cookie);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CookieValue(response).Should().StartWith("p.").And.NotBe(session.Cookie);
        ShouldSetTheSessionCookie(response, await PlatformExpiryAsync(CookieValue(response)));
        await ShouldNotCarryARefreshTokenAsync(response);
    }

    [Theory]
    [InlineData("/api/platform/auth/mfa/verify", "mfaChallenge")]
    [InlineData("/api/auth/mfa/verify", "challenge")]
    public async Task A_platform_second_step_sets_the_cookie_on_either_route(string route, string challengeField)
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();
        var secret = await EnablePlatformTwoStepAsync(email, password);
        var first = await _kit.PostAsync("/api/platform/auth/login", new { email, password });
        ShouldSetNoCookie(first);
        var challenge = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaChallenge").GetString();

        var body = new Dictionary<string, string?> { [challengeField] = challenge, ["code"] = CodeAt(secret, 1) };
        var verified = await _kit.PostAsync(route, body);

        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        ShouldSetTheSessionCookie(verified, await PlatformExpiryAsync(CookieValue(verified)));
        await ShouldNotCarryARefreshTokenAsync(verified);
    }

    // ── SameSite comes from the configuration ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("None", "none")]
    [InlineData("Strict", "strict")]
    [InlineData("lax", "lax")]
    public async Task SameSite_is_what_the_configuration_says(string setting, string expected)
    {
        await using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:Session:CookieSameSite", setting));
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        var response = await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));

        ShouldSetTheSessionCookie(response, TokenExpiry(CookieValue(response)), expected);
    }

    [Fact]
    public void A_SameSite_that_does_not_exist_stops_the_startup()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:Session:CookieSameSite", "Nenhum"));

        var start = () => host.CreateClient();

        start.Should().Throw<Exception>().Where(e => e.ToString().Contains("CookieSameSite"));
    }

    // ── Ending a session ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_works_with_the_cookie_alone_from_a_tab_that_has_no_access_token()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var response = await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        ShouldExpireTheSessionCookie(response);
        ShouldExpireTheOldCookies(response);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the family ended on the server");
    }

    [Fact]
    public async Task Logout_with_an_expired_access_token_still_ends_the_session()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var response = await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie, bearer: Expired(session.Access));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ends_a_session_a_concurrent_renewal_just_moved_on()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);
        var renewed = await _kit.RenewAsync(session.Cookie);

        // the cookie the browser sent is the old one: the answer of the renewal has not arrived
        var response = await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _kit.RenewAsync(CookieValue(renewed))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "logout ends the whole family");
    }

    [Fact]
    public async Task Logout_without_a_cookie_in_the_cookie_mode_answers_the_same_and_removes_nothing_it_cannot_see()
    {
        var response = await _kit.PostAsync("/api/auth/logout");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShouldExpireTheSessionCookie(response);
    }

    [Fact]
    public async Task Logout_of_a_platform_session_with_the_cookie_alone_ends_it()
    {
        var session = await PlatformSignInAsync();

        var response = await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        ShouldExpireTheSessionCookie(response);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", bearer: session.Access)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a platform session ends at once, its access token included");
    }

    [Fact]
    public async Task Revoke_all_removes_the_cookie()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var response = await _kit.PostAsync("/api/auth/sessions/revoke-all", new { confirm = true }, sessionCookie: session.Cookie, bearer: session.Access);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShouldExpireTheSessionCookie(response);
        ShouldExpireTheOldCookies(response);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Paths_that_end_every_session_of_the_person_remove_the_cookie_too()
    {
        var mfa = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(mfa.Login, mfa.Password);
        var setup = await _kit.PostAsync("/api/auth/mfa/setup", new { password = mfa.Password }, bearer: session.Access);
        var secret = FromBase32((await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!);
        var enabled = await _kit.PostAsync("/api/auth/mfa/enable", new { code = CodeAt(secret, 0) }, sessionCookie: session.Cookie, bearer: session.Access);
        enabled.StatusCode.Should().Be(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync());
        ShouldExpireTheSessionCookie(enabled);

        var password = await _kit.NewAccountAsync();
        var other = await _kit.SignInAsync(password.Login, password.Password);
        var changed = await _kit.PostAsync($"/api/users/{password.UserId}/change-password",
            new { currentPassword = password.Password, newPassword = "n" + Guid.NewGuid().ToString("N") }, sessionCookie: other.Cookie, bearer: other.Access);
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());
        ShouldExpireTheSessionCookie(changed);

        var platform = await PlatformSignInAsync();
        var revoked = await _kit.PostAsync("/api/platform/auth/sessions/revoke-all", sessionCookie: platform.Cookie, bearer: platform.Access);
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent, await revoked.Content.ReadAsStringAsync());
        ShouldExpireTheSessionCookie(revoked);
    }

    [Fact]
    public async Task A_refused_action_does_not_remove_the_cookie()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var wrong = await _kit.PostAsync($"/api/users/{account.UserId}/change-password",
            new { currentPassword = "e" + Guid.NewGuid().ToString("N"), newPassword = "n" + Guid.NewGuid().ToString("N") }, sessionCookie: session.Cookie, bearer: session.Access);

        wrong.IsSuccessStatusCode.Should().BeFalse();
        ShouldSetNoCookie(wrong);
    }

    [Fact]
    public async Task Without_the_header_a_refused_renewal_says_session_ended_and_with_it_says_session_revoked_when_the_stamp_changed()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);
        var legacy = await LegacySignInAsync(account);
        await BumpStampAsync(account.UserId);

        var cookieMode = await _kit.RenewAsync(session.Cookie);
        var storageMode = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(legacy.RefreshToken), mode: null);

        cookieMode.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(cookieMode)).Should().Be("session_revoked", "a tab that just reloaded has no access token to hear it from");
        storageMode.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(storageMode)).Should().Be("session_ended", "a request without the header is answered as always");
    }

    [Fact]
    public async Task A_change_made_by_a_director_reaches_a_tab_that_reloads_as_session_revoked()
    {
        var director = await _kit.NewAccountAsync();
        var target = await SyntheticTenant.AddUserAsync(_factory, director, Nexo.Domain.Enums.UserRole.Gerente);
        var session = await _kit.SignInAsync(target.Login, target.Password);
        var managing = await LegacySignInAsync(director);

        var changed = await _kit.SendAsync(HttpMethod.Put, $"/api/users/{target.UserId}",
            new { fullName = target.FullName, email = target.Email, role = "vendedor", phone = (string?)null, notes = (string?)null, status = "active" },
            bearer: managing.AccessToken, mode: null);
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        var renewal = await _kit.RenewAsync(session.Cookie);

        renewal.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(renewal)).Should().Be("session_revoked");
    }

    [Fact]
    public async Task A_sign_out_elsewhere_reaches_a_tab_that_reloads_as_session_ended()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);
        (await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var renewal = await _kit.RenewAsync(session.Cookie);

        renewal.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeOfAsync(renewal)).Should().Be("session_ended");
    }

    // ── Migration of a session that is still in localStorage ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_body_token_with_the_header_migrates_the_session_to_the_cookie()
    {
        var account = await _kit.NewAccountAsync();
        var legacy = await LegacySignInAsync(account);

        var migrated = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(legacy.RefreshToken));

        migrated.StatusCode.Should().Be(HttpStatusCode.OK, await migrated.Content.ReadAsStringAsync());
        ShouldSetTheSessionCookie(migrated, TokenExpiry(CookieValue(migrated)));
        await ShouldNotCarryARefreshTokenAsync(migrated);

        // the token of localStorage was spent by the rotation, the same as any other renewal
        var replay = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(legacy.RefreshToken));
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _kit.RenewAsync(CookieValue(migrated))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refused_migration_leaves_a_cookie_that_is_already_there_alone()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var refused = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload("not-a-token"), sessionCookie: session.Cookie);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldSetNoCookie(refused);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_platform_token_in_the_body_with_the_header_migrates_too()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();
        var legacy = await _kit.PostAsync("/api/platform/auth/login", new { email, password }, mode: null);
        var token = (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;

        var migrated = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(token));

        migrated.StatusCode.Should().Be(HttpStatusCode.OK, await migrated.Content.ReadAsStringAsync());
        ShouldSetTheSessionCookie(migrated, await PlatformExpiryAsync(CookieValue(migrated)));
        await ShouldNotCarryARefreshTokenAsync(migrated);
    }

    // ── CSRF: a refused Origin reads nothing and changes nothing ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task Refresh_from_a_refused_origin_does_not_rotate(string? origin)
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        await ShouldBeRefusedByOriginAsync(await _kit.RenewAsync(session.Cookie, origin));

        // the token was not spent: rotated, it would answer 409 here
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task Logout_from_a_refused_origin_ends_nothing(string? origin)
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        await ShouldBeRefusedByOriginAsync(await _kit.PostAsync("/api/auth/logout", origin: origin, sessionCookie: session.Cookie, bearer: session.Access));

        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task Revoke_all_from_a_refused_origin_ends_nothing(string? origin)
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        await ShouldBeRefusedByOriginAsync(await _kit.PostAsync("/api/auth/sessions/revoke-all", new { confirm = true }, origin, session.Cookie, session.Access));

        (await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", bearer: session.Access, mode: null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task Switch_store_from_a_refused_origin_changes_nothing(string? origin)
    {
        var account = await _kit.NewAccountAsync();
        var second = await AddStoreAsync(account);
        var session = await _kit.SignInAsync(account.Login, account.Password);

        await ShouldBeRefusedByOriginAsync(await _kit.PostAsync("/api/auth/switch-store", new { storeId = second }, origin, session.Cookie, session.Access));

        var renewed = await _kit.RenewAsync(session.Cookie);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        ClaimOf((await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!, "storeId").Should().NotBe(second.ToString());
    }

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task Login_from_a_refused_origin_checks_no_password_and_opens_no_session(string? origin)
    {
        var account = await _kit.NewAccountAsync();
        var before = await SessionsOfAsync(account.UserId);

        await ShouldBeRefusedByOriginAsync(await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), origin));

        (await SessionsOfAsync(account.UserId)).Should().Be(before);
    }

    [Fact]
    public async Task Wrong_passwords_from_a_refused_origin_never_lock_the_account()
    {
        var account = await _kit.NewAccountAsync();

        for (var attempt = 0; attempt < 12; attempt++)
            await ShouldBeRefusedByOriginAsync(await _kit.PostAsync("/api/auth/login",
                TestCredentials.LoginPayload(account.Login, "e" + Guid.NewGuid().ToString("N")), "https://evil.orken.com.br"));

        var session = await _kit.SignInAsync(account.Login, account.Password);
        session.Cookie.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task A_second_step_from_a_refused_origin_does_not_spend_the_challenge_nor_the_code(string? origin)
    {
        var account = await _kit.NewAccountAsync();
        var (secret, _) = await EnableTwoStepAsync(account);
        var first = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));
        var challenge = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaChallenge").GetString();
        var code = CodeAt(secret, 1);

        await ShouldBeRefusedByOriginAsync(await _kit.PostAsync("/api/auth/mfa/verify", new { challenge, code }, origin));

        (await _kit.PostAsync("/api/auth/mfa/verify", new { challenge, code })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task Verify_email_from_a_refused_origin_does_not_spend_the_link(string? origin)
    {
        var token = await NewPendingEmailLinkAsync();

        await ShouldBeRefusedByOriginAsync(await _kit.SendAsync(HttpMethod.Get, $"/api/auth/verify-email?token={token}", origin: origin));

        (await _kit.SendAsync(HttpMethod.Get, $"/api/auth/verify-email?token={token}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/platform/auth/login")]
    [InlineData("/api/platform/auth/mfa/verify")]
    [InlineData("/api/platform/auth/refresh")]
    [InlineData("/api/platform/auth/logout")]
    [InlineData("/api/platform/auth/sessions/revoke-all")]
    public async Task Platform_routes_refuse_a_foreign_origin_too(string route)
    {
        var session = await PlatformSignInAsync();

        await ShouldBeRefusedByOriginAsync(await _kit.PostAsync(route, new { email = "x@platform.test", password = "x" }, "https://evil.orken.com.br", session.Cookie, session.Access));

        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refused_origin_spends_no_permit_of_the_rate_limiter()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
            b.UseSetting("RateLimiting:AuthLogin:PermitLimit", "1");
        });
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();
        var address = "203.0.113." + RandomNumberGenerator.GetInt32(1, 250);

        for (var attempt = 0; attempt < 3; attempt++)
            await ShouldBeRefusedByOriginAsync(await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), "https://evil.example", clientIp: address));

        var allowed = await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), clientIp: address);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── The cookie alone, with no header, has no authority at all ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("yes")]
    public async Task The_cookie_without_the_header_does_not_renew(string? mode)
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var response = await _kit.PostAsync("/api/auth/refresh", sessionCookie: session.Cookie, mode: mode);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ShouldSetNoCookie(response);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("accessToken");
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK, "and the token was not spent");
    }

    [Fact]
    public async Task The_cookie_without_the_header_does_not_end_the_session()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        (await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie, mode: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "without the header logout asks for the access token, as it always did");
        var signedIn = await _kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie, bearer: session.Access, mode: null);
        signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookies(signedIn).Should().NotContain(c => c.StartsWith(CookieName, StringComparison.Ordinal), "no authority over the cookie either");

        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK, "the cookie was not read");
    }

    [Fact]
    public async Task The_cookie_without_the_header_does_not_switch_the_store()
    {
        var account = await _kit.NewAccountAsync();
        var second = await AddStoreAsync(account);
        var session = await _kit.SignInAsync(account.Login, account.Password);

        var response = await _kit.PostAsync("/api/auth/switch-store", new { storeId = second }, sessionCookie: session.Cookie, bearer: session.Access, mode: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldSetNoCookie(response);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_cookie_is_not_an_access_token()
    {
        var account = await _kit.NewAccountAsync();
        var session = await _kit.SignInAsync(account.Login, account.Password);

        (await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", sessionCookie: session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", sessionCookie: session.Access)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── The old cookies: nothing reads them ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cookie")]
    [InlineData(null)]
    public async Task The_old_refresh_cookie_is_not_read(string? mode)
    {
        var account = await _kit.NewAccountAsync();
        var legacy = await LegacySignInAsync(account);

        var response = await _kit.PostAsync("/api/auth/refresh", mode: mode, rawCookies: $"nexo_refresh={legacy.RefreshToken}");

        response.IsSuccessStatusCode.Should().BeFalse();
        ShouldSetNoCookie(response);
        (await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(legacy.RefreshToken), mode: null)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the token was not spent");
    }

    [Fact]
    public async Task The_old_refresh_cookie_does_not_switch_the_store_nor_end_the_session()
    {
        var account = await _kit.NewAccountAsync();
        var second = await AddStoreAsync(account);
        var legacy = await LegacySignInAsync(account);
        var old = $"nexo_refresh={legacy.RefreshToken}";

        var switched = await _kit.PostAsync("/api/auth/switch-store", new { storeId = second }, bearer: legacy.AccessToken, mode: null, rawCookies: old);
        switched.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var signedOut = await _kit.PostAsync("/api/auth/logout", bearer: legacy.AccessToken, mode: null, rawCookies: old);
        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(legacy.RefreshToken), mode: null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task No_response_writes_the_old_cookies_any_more()
    {
        var account = await _kit.NewAccountAsync();

        var login = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), mode: null);
        ShouldSetNoCookie(login);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();

        var refresh = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(body!.RefreshToken), mode: null);
        ShouldSetNoCookie(refresh);
    }

    /// <summary>
    /// The fallback is gone from every route, not only from the one a test happens to call: each endpoint that asks for a session
    /// gets a valid token in the old cookie and has to answer 401, as if nothing had come. A route added later is in this list by itself.
    /// </summary>
    [Fact]
    public async Task The_old_access_cookie_opens_no_route_at_all()
    {
        var account = await _kit.NewAccountAsync();
        var tenant = await LegacySignInAsync(account);
        var platform = await PlatformSignInAsync();

        (await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", bearer: tenant.AccessToken, mode: null)).StatusCode.Should().Be(HttpStatusCode.OK, "control: the token itself is good");

        var opened = new List<string>();
        var swept = 0;
        foreach (var (method, path, multipart) in ProtectedRoutes())
        {
            var token = path.StartsWith("/api/platform", StringComparison.OrdinalIgnoreCase) ? platform.Access : tenant.AccessToken;
            // an upload route only matches its content type: anything else is a 415 from the routing, before any authentication counts
            object? body = method is "GET" or "DELETE" ? null : multipart ? new MultipartFormDataContent { { new StringContent("x"), "x" } } : new { };
            var response = await _kit.SendAsync(new HttpMethod(method), path, body, origin: null, mode: null, rawCookies: $"nexo_access={token}");
            swept++;
            if (response.StatusCode != HttpStatusCode.Unauthorized) opened.Add($"{method} {path} -> {(int)response.StatusCode}");
        }

        swept.Should().BeGreaterThan(300, "a sweep that finds few routes proves little");
        opened.Should().BeEmpty();
    }

    [Fact]
    public async Task The_old_access_cookie_opens_no_hub()
    {
        var account = await _kit.NewAccountAsync();
        var tenant = await LegacySignInAsync(account);

        var negotiation = await _kit.PostAsync("/hubs/restaurant/negotiate?negotiateVersion=1", origin: null, mode: null, rawCookies: $"nexo_access={tenant.AccessToken}");

        negotiation.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Without the header, everything as it was ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_the_header_the_refresh_token_stays_in_the_body_on_every_path()
    {
        var account = await _kit.NewAccountAsync();
        var second = await AddStoreAsync(account);

        var login = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), mode: null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(login);
        var signedIn = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        signedIn.RefreshToken.Should().NotBeNullOrEmpty();

        var renewed = await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(signedIn.RefreshToken), mode: null);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(renewed);
        var tokens = (await renewed.Content.ReadFromJsonAsync<RefreshResponse>())!;
        tokens.RefreshToken.Should().NotBe(signedIn.RefreshToken);

        var switched = await _kit.PostAsync("/api/auth/switch-store", new { storeId = second, refreshToken = tokens.RefreshToken }, bearer: tokens.AccessToken, mode: null);
        switched.StatusCode.Should().Be(HttpStatusCode.OK, await switched.Content.ReadAsStringAsync());
        ShouldSetNoCookie(switched);
        var moved = (await switched.Content.ReadFromJsonAsync<SwitchStoreResponse>())!;
        moved.RefreshToken.Should().NotBeNullOrEmpty();

        var logout = await _kit.PostAsync("/api/auth/logout", new { refreshToken = moved.RefreshToken }, bearer: moved.AccessToken, mode: null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShouldExpireTheOldCookies(logout);
        SetCookies(logout).Should().NotContain(c => c.StartsWith(CookieName, StringComparison.Ordinal));
        (await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(moved.RefreshToken), mode: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Without_the_header_an_empty_renewal_is_still_a_400()
    {
        (await _kit.PostAsync("/api/auth/refresh", mode: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _kit.PostAsync("/api/auth/refresh", new { }, mode: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _kit.PostAsync("/api/auth/refresh", new { refreshToken = "" }, mode: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Without_the_header_no_Origin_is_needed()
    {
        var account = await _kit.NewAccountAsync();

        var login = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), origin: null, mode: null);

        login.StatusCode.Should().Be(HttpStatusCode.OK, "server to server and old clients go on as before");
    }

    [Fact]
    public async Task Without_the_header_platform_tokens_stay_in_the_body()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();

        var login = await _kit.PostAsync("/api/platform/auth/login", new { email, password }, mode: null);
        ShouldSetNoCookie(login);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        var renewed = await _kit.PostAsync("/api/platform/auth/refresh", new { refreshToken = body.GetProperty("refreshToken").GetString() }, mode: null);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(renewed);
        (await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString().Should().StartWith("p.");
    }

    // ── One list of origins, for CORS and for the check ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Outside_development_localhost_is_not_an_origin(string environment)
    {
        await using var host = HostIn(environment);
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        var preflight = await PreflightAsync(kit, "http://localhost:5173");
        preflight.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();

        await ShouldBeRefusedByOriginAsync(await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), "http://localhost:5173"));
    }

    [Fact]
    public async Task In_development_localhost_is_an_origin_for_both()
    {
        await using var host = HostIn("Development");
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        (await PreflightAsync(kit, "http://localhost:5173")).Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("http://localhost:5173");
        (await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), "http://localhost:5173")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("https://erp.orken.com.br")]
    [InlineData("https://www.orken.com.br")]
    [InlineData("https://erp.staging.example")]
    public async Task A_listed_origin_passes_both(string origin)
    {
        await using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Cors:AllowedOrigins:0", "https://erp.staging.example"));
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        var preflight = await PreflightAsync(kit, origin);
        preflight.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
        preflight.Headers.GetValues("Access-Control-Allow-Credentials").Should().Equal("true");

        var login = await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), origin);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
    }

    [Fact]
    public async Task A_foreign_origin_gets_no_CORS_headers_on_the_preflight()
    {
        var preflight = await PreflightAsync(_kit, "https://evil.orken.com.br");

        preflight.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        preflight.Headers.Contains("Access-Control-Allow-Headers").Should().BeFalse();
    }

    [Fact]
    public void A_wildcard_in_the_list_stops_the_startup()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Cors:AllowedOrigins:0", "*"));

        var start = () => host.CreateClient();

        start.Should().Throw<Exception>();
    }

    /// <summary>Every header a client of this repository sends across origins. A new one fails in the browser before it fails here.</summary>
    [Theory]
    [InlineData("content-type")]
    [InlineData("authorization")]
    [InlineData("x-orken-session")]
    [InlineData("x-requested-with")]
    [InlineData("x-signalr-user-agent")]
    [InlineData("x-build-portal-session")]
    [InlineData("if-none-match")]
    public async Task The_headers_of_the_clients_pass_the_preflight(string header)
    {
        var preflight = await PreflightAsync(_kit, AppOrigin, header);

        preflight.Headers.GetValues("Access-Control-Allow-Headers").SelectMany(v => v.Split(',')).Select(h => h.Trim().ToLowerInvariant())
            .Should().Contain(header);
    }

    [Fact]
    public async Task A_header_no_client_sends_does_not_pass_the_preflight()
    {
        var preflight = await PreflightAsync(_kit, AppOrigin, "x-made-up");

        var allowed = preflight.Headers.TryGetValues("Access-Control-Allow-Headers", out var values) ? values.SelectMany(v => v.Split(',')) : [];
        allowed.Select(h => h.Trim().ToLowerInvariant()).Should().NotContain("x-made-up");
    }

    [Fact]
    public async Task No_exposed_header_carries_a_credential()
    {
        var response = await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", origin: AppOrigin, mode: null);

        response.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(v => v.Split(',')).Select(h => h.Trim())
            .Should().BeEquivalentTo("Content-Disposition", "Content-Length", "Retry-After", "ETag", "X-Request-Id");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> PreflightAsync(BrowserSessionKit kit, string origin, string requestHeaders = "content-type,x-orken-session")
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", requestHeaders);
        return await kit.Client().SendAsync(request);
    }

    /// <summary>Every route that asks for a session, with its parameters filled in, method by method.</summary>
    private IEnumerable<(string Method, string Path, bool Multipart)> ProtectedRoutes()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) continue;
            if (endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0) continue;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            var multipart = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes
                .Any(type => type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)) == true;
            foreach (var method in methods.Where(m => m != "OPTIONS"))
                yield return (method, Fill(endpoint), multipart);
        }
    }

    private static string Fill(RouteEndpoint endpoint)
    {
        var segments = endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternSeparatorPart separator => separator.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter => SampleOf(parameter, endpoint),
            _ => "",
        })));
        return "/" + string.Join('/', segments);
    }

    private static string SampleOf(Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter, RouteEndpoint endpoint)
    {
        var constraints = endpoint.RoutePattern.ParameterPolicies.TryGetValue(parameter.Name, out var policies)
            ? string.Join(",", policies.Select(p => p.Content))
            : "";
        if (constraints.Contains("guid", StringComparison.OrdinalIgnoreCase)) return Guid.NewGuid().ToString();
        if (constraints.Contains("int", StringComparison.OrdinalIgnoreCase)) return "1";
        return "amostra";
    }

    private async Task<LoginResponse> LegacySignInAsync(SyntheticAccount account)
    {
        var response = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), mode: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<CookieSession> PlatformSignInAsync()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();
        return await _kit.SignInAsync(email, password);
    }

    private async Task<DateTime> PlatformExpiryAsync(string refreshToken)
    {
        var hash = Nexo.Infrastructure.Auth.PlatformAuthService.Hash(refreshToken);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.PlatformSessions.AsNoTracking().Where(s => s.RefreshTokenHash == hash).Select(s => s.ExpiresAt).SingleAsync();
    }

    private async Task<Guid> AddStoreAsync(SyntheticAccount account)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var store = Store.Create(account.TenantId, $"Segunda {tag}", $"segunda-{tag}");
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }

    private async Task<int> SessionsOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.UserSessions.IgnoreQueryFilters().CountAsync(s => s.UserId == userId);
    }

    private async Task BumpStampAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        user.BumpSecurityStamp();
        await db.SaveChangesAsync();
    }

    private async Task<string> NewPendingEmailLinkAsync()
    {
        var email = $"ck{Guid.NewGuid():N}"[..14] + "@nexo.test";
        var registered = await _kit.Client().PostAsJsonAsync("/api/auth/register", new { name = "Empresa de teste", email, password = "t" + Guid.NewGuid().ToString("N") });
        registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
        var link = Guid.NewGuid().ToString("N");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
        user.SetVerificationToken(link, DateTime.UtcNow.AddMinutes(30));
        await db.SaveChangesAsync();
        return link;
    }

    private async Task<(byte[] Secret, string[] RecoveryCodes)> EnableTwoStepAsync(SyntheticAccount account)
    {
        var legacy = await LegacySignInAsync(account);
        var setup = await _kit.PostAsync("/api/auth/mfa/setup", new { password = account.Password }, bearer: legacy.AccessToken, mode: null);
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        var secret = FromBase32((await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!);
        var enabled = await _kit.PostAsync("/api/auth/mfa/enable", new { code = CodeAt(secret, 0) }, bearer: legacy.AccessToken, mode: null);
        enabled.StatusCode.Should().Be(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync());
        return (secret, (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray());
    }

    private async Task<byte[]> EnablePlatformTwoStepAsync(string email, string password)
    {
        var login = await _kit.PostAsync("/api/platform/auth/login", new { email, password }, mode: null);
        var access = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var setup = await _kit.PostAsync("/api/platform/auth/mfa/setup", new { password }, bearer: access, mode: null);
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        var secret = FromBase32((await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!);
        (await _kit.PostAsync("/api/platform/auth/mfa/enable", new { code = CodeAt(secret, 0) }, bearer: access, mode: null)).StatusCode.Should().Be(HttpStatusCode.OK);
        return secret;
    }

    private static string CodeAt(byte[] secret, int stepOffset) => Totp.Code(secret, Totp.StepAt(DateTime.UtcNow) + stepOffset);

    private static byte[] FromBase32(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }

    /// <summary>The same token, correctly signed, past its expiry.</summary>
    private string Expired(string accessToken)
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        string[] lifetime = ["exp", "nbf", "iat", "iss", "aud"];
        var now = DateTime.UtcNow;
        var expired = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: token.Claims.Where(c => !lifetime.Contains(c.Type)).Select(c => new System.Security.Claims.Claim(c.Type, c.Value)),
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(expired);
    }

    /// <summary>The real startup in another environment: strict ones demand a random signing secret.</summary>
    private WebApplicationFactory<Program> HostIn(string environment)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            // Outside "Testing" the periodic jobs start too: they must not touch the data of other tests in the collection.
            b.ConfigureTestServices(s =>
            {
                foreach (var job in s.Where(d => d.ServiceType == typeof(IHostedService)
                                                 && d.ImplementationType?.Namespace?.StartsWith("Nexo.", StringComparison.Ordinal) == true).ToList())
                    s.Remove(job);
            });
        });
}
