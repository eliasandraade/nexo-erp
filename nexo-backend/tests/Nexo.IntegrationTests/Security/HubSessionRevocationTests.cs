using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Hubs;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Security.RedisSessionKit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// An open connection to the hub when the session behind it ends (issue #137 applied to SignalR). The real hub, real events from
/// the real endpoints, a real SignalR client and a real Redis. The response to the change is the cut-off point: from then on the
/// person's connection is closed and no event reaches it, without any help from the client.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class HubSessionRevocationTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private readonly RedisWebApplicationFactory _factory;
    private readonly RedisSessionKit _sessions;
    private readonly HubKit _hub;

    public HubSessionRevocationTests(RedisWebApplicationFactory factory)
    {
        _factory = factory;
        _sessions = new RedisSessionKit(factory);
        _hub = new HubKit(factory);
    }

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task UpdateAsync(RealtimeCompany company, SyntheticAccount target, string role, string status = "active", string? fullName = null)
        => company.Director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, role, status, fullName: fullName))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

    /// <summary>A person connected and listening, with a witness of the same store who is not touched by the change.</summary>
    private async Task<(RealtimeCompany Company, SyntheticAccount Target, HubProbe TargetProbe, HubProbe Witness)> ListeningAsync(
        UserRole role, HubTransport transport = HubTransport.WebSockets)
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, role);
        var targetProbe = await _hub.ListenAsAsync(company, target, transport);
        var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.BothReceiveAsync(company, targetProbe, witness);
        return (company, target, targetProbe, witness);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // WHAT ENDS A SESSION
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HubTransport.WebSockets)]
    [InlineData(HubTransport.LongPolling)]
    public async Task Demotion_closes_the_connection_and_the_next_event_does_not_reach_it(HubTransport transport)
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Gerente, transport);
        await using (probe)
        await using (witness)
        {
            await UpdateAsync(company, target, "vendedor");

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    [Fact]
    public async Task Promotion_closes_the_connection_too_because_the_token_carries_the_old_role()
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            await UpdateAsync(company, target, "gerente");

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("inactive")]
    public async Task Blocking_or_inactivating_closes_the_connection(string status)
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            await UpdateAsync(company, target, "vendedor", status);

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    [Fact]
    public async Task Signing_out_of_every_device_closes_the_connection()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var (client, login) = await _sessions.SignInAsync(target);
        await using var probe = await _hub.ListenAsync(company, login.AccessToken);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.BothReceiveAsync(company, probe, witness);

        await client.PostAsJsonAsync("/api/auth/sessions/revoke-all", new { confirm = true }).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
    }

    [Fact]
    public async Task Changing_your_own_password_closes_the_connection()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var (client, login) = await _sessions.SignInAsync(target);
        await using var probe = await _hub.ListenAsync(company, login.AccessToken);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.BothReceiveAsync(company, probe, witness);

        await client.PostAsJsonAsync($"{UsersRoute}/{target.UserId}/change-password",
                new { currentPassword = target.Password, newPassword = "OutraSenhaDeTeste!1" })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
    }

    [Fact]
    public async Task A_manager_resetting_the_password_closes_the_connection()
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            await company.Director.PostAsJsonAsync($"{UsersRoute}/{target.UserId}/admin-reset-password", new { newPassword = "OutraSenhaDeTeste!1" })
                .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    [Fact]
    public async Task The_password_reset_link_closes_the_connection()
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            const string token = "link-de-teste-do-hub";
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == target.UserId);
                user.SetPasswordResetToken(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(), DateTime.UtcNow.AddHours(1));
                await db.SaveChangesAsync();
            }

            await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "OutraSenhaDeTeste!1" })
                .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    public enum PlatformAct { ForceLogout, RevokeAllSessions, ResetPassword }

    [Theory]
    [InlineData(PlatformAct.ForceLogout)]
    [InlineData(PlatformAct.RevokeAllSessions)]
    [InlineData(PlatformAct.ResetPassword)]
    public async Task The_platform_ending_the_sessions_of_a_person_closes_the_connection(PlatformAct act)
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            var platform = await _sessions.PlatformClientAsync();
            var user = $"/api/platform/tenants/{company.TenantId}/users/{target.UserId}";
            await (act switch
            {
                PlatformAct.ForceLogout => platform.PostAsync($"{user}/force-logout", null),
                PlatformAct.RevokeAllSessions => platform.DeleteAsync($"{user}/sessions"),
                _ => platform.PostAsJsonAsync($"{user}/reset-password", new { newPassword = "OutraSenhaDeTeste!1" }),
            }).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    [Fact]
    public async Task Any_commit_that_rotates_the_stamp_closes_the_connection_whichever_code_made_it()
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == target.UserId);
                user.BumpSecurityStamp();
                await db.SaveChangesAsync();
            }

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    [Fact]
    public async Task A_rotation_inside_a_transaction_closes_the_connection_only_after_the_commit()
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

            // rolled back: nothing changed, so nothing closes
            await using (var undone = await db.Database.BeginTransactionAsync())
            {
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == target.UserId);
                user.BumpSecurityStamp();
                await db.SaveChangesAsync();
                await undone.RollbackAsync();
            }
            db.ChangeTracker.Clear();
            await _hub.BothReceiveAsync(company, probe, witness);

            await using (var kept = await db.Database.BeginTransactionAsync())
            {
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == target.UserId);
                user.BumpSecurityStamp();
                await db.SaveChangesAsync();
                // saved, not committed: the old stamp is still the valid one
                await _hub.BothReceiveAsync(company, probe, witness);
                await kept.CommitAsync();
            }

            await _hub.ShouldNoLongerReceiveAsync(company, probe, witness);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // WHO IS AFFECTED
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_connection_of_the_person_closes_and_those_of_other_people_stay()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Gerente);
        var bystander = await _hub.AddPersonAsync(company, UserRole.Gerente);

        // two devices (two sign-ins) and two tabs of one of them
        var firstDevice = (await _hub.SignInAsync(target)).AccessToken;
        await using var deviceOneTabOne = await _hub.ListenAsync(company, firstDevice);
        await using var deviceOneTabTwo = await _hub.ListenAsync(company, firstDevice);
        await using var deviceTwo = await _hub.ListenAsAsync(company, target);
        await using var bystanderProbe = await _hub.ListenAsAsync(company, bystander);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.BothReceiveAsync(company, deviceOneTabOne, deviceOneTabTwo, deviceTwo, bystanderProbe, witness);

        await UpdateAsync(company, target, "vendedor");

        await _hub.ShouldNoLongerReceiveAsync(company, deviceOneTabOne, witness);
        await deviceOneTabTwo.ClosedAsync();
        await deviceTwo.ClosedAsync();

        var after = await _hub.EmitEventAsync(company);
        await bystanderProbe.SawAsync(after);
        await witness.SawAsync(after);
        bystanderProbe.IsClosed.Should().BeFalse();
        deviceOneTabTwo.Saw(after).Should().BeFalse();
        deviceTwo.Saw(after).Should().BeFalse();
    }

    [Fact]
    public async Task Editing_the_name_keeps_the_connection_up_because_the_stamp_did_not_change()
    {
        var (company, target, probe, witness) = await ListeningAsync(UserRole.Vendedor);
        await using (probe)
        await using (witness)
        {
            var stampBefore = await _sessions.SecurityStampOfAsync(target.UserId);

            await UpdateAsync(company, target, "vendedor", fullName: "Nome Novo Da Pessoa");

            (await _sessions.SecurityStampOfAsync(target.UserId)).Should().Be(stampBefore);
            await _hub.BothReceiveAsync(company, probe, witness);
            probe.IsClosed.Should().BeFalse();
            await probe.LeaveAsync(company.StoreId);
            await probe.JoinAsync(company.StoreId);
            await _hub.BothReceiveAsync(company, probe, witness);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // COMING BACK
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnecting_with_the_old_token_is_refused(bool skipNegotiation)
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Gerente);
        var old = (await _hub.SignInAsync(target)).AccessToken;
        await using (var probe = await _hub.ListenAsync(company, old))
        {
            await UpdateAsync(company, target, "vendedor");
            await probe.ClosedAsync();
        }

        var refused = () => HubProbe.ConnectAsync(_hub.Server, old, skipNegotiation: skipNegotiation);

        await refused.Should().ThrowAsync<Exception>("the old token died with the stamp, on the negotiation and on the socket itself");
    }

    [Fact]
    public async Task A_new_token_of_a_role_outside_the_hub_is_refused()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Gerente);
        await using (var probe = await _hub.ListenAsAsync(company, target))
        {
            await UpdateAsync(company, target, "estoquista");
            await probe.ClosedAsync();
        }

        var (client, login) = await _sessions.SignInAsync(target);
        login.Session.Role.Should().Be("estoquista");
        await client.PostAsync(HubNegotiation, null).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        var refused = () => HubProbe.ConnectAsync(_hub.Server, login.AccessToken, skipNegotiation: true);
        await refused.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task A_new_token_of_an_allowed_role_connects_and_a_late_signal_does_not_close_it()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Gerente);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await using (var probe = await _hub.ListenAsAsync(company, target))
        {
            await UpdateAsync(company, target, "vendedor");
            await probe.ClosedAsync();
        }

        await using var renewed = await _hub.ListenAsAsync(company, target);
        await _hub.BothReceiveAsync(company, renewed, witness);

        // the signal of the old change reaching this instance late judges the connection by the database, not by the message
        var signals = _factory.Services.GetRequiredService<HubSessionSignals>();
        await signals.PublishAsync(new HubSessionChange([target.UserId], []), CancellationToken.None);

        renewed.IsClosed.Should().BeFalse();
        await _hub.BothReceiveAsync(company, renewed, witness);
    }

    [Fact]
    public async Task A_token_without_a_stamp_cannot_stay_connected()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = HubKit.WithLifetime((await _hub.SignInAsync(target)).AccessToken, TimeSpan.FromMinutes(5), keep: c => c.Type != "security_stamp");

        await using var probe = await HubProbe.ConnectAsync(_hub.Server, token);

        await probe.ClosedAsync();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // A CLIENT THAT DOES NOT COOPERATE
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_modified_client_that_ignores_the_close_receives_nothing_more()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Gerente);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);
        await using var raw = await RawHubClient.ConnectAsync(_hub.Server, _factory.CreateApiClient(), (await _hub.SignInAsync(target)).AccessToken);
        await raw.InvokeAsync("JoinStore", company.StoreId.ToString());
        await raw.WaitForFrameAsync(frame => frame.Contains("\"type\":3", StringComparison.Ordinal));
        var before = await _hub.EmitEventAsync(company);
        await raw.WaitForFrameAsync(frame => frame.Contains(before.ToString(), StringComparison.Ordinal));
        await witness.SawAsync(before);

        await UpdateAsync(company, target, "vendedor");

        var after = await _hub.EmitEventAsync(company);
        await witness.SawAsync(after);
        await raw.ServerEndedTheSocketAsync();
        raw.Frames.Should().NotContain(frame => frame.Contains(after.ToString(), StringComparison.Ordinal),
            "the server wrote nothing about this event on that socket");
        var close = JsonDocument.Parse(raw.Frames.Single(frame => frame.Contains("\"type\":7", StringComparison.Ordinal))).RootElement;
        (close.TryGetProperty("allowReconnect", out var allow) && allow.GetBoolean()).Should().BeFalse("a revoked session is told not to reconnect");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // TENANT SUSPENSION AND IMPERSONATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Suspending_the_company_closes_all_its_connections_and_no_others_and_reactivating_lets_them_back()
    {
        var company = await _hub.NewCompanyAsync();
        var other = await _hub.NewCompanyAsync();
        var person = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        await using var probe = await _hub.ListenAsAsync(company, person);
        await using var director = await _hub.ListenAsync(company, company.DirectorToken);
        await using var otherProbe = await _hub.ListenAsync(other, other.DirectorToken);
        await _hub.BothReceiveAsync(company, probe, director);
        var group = RestaurantHub.GroupFor(company.TenantId, company.StoreId);
        var platform = await _sessions.PlatformClientAsync();

        await platform.PutAsJsonAsync($"/api/platform/tenants/{company.TenantId}/status", new { status = "Suspended" })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        // what the HTTP path says for the same company: the access is over
        await company.Director.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        // the HTTP path of the company is closed, so an in-flight event goes straight through the hub
        var hub = _factory.Services.GetRequiredService<IHubContext<RestaurantHub>>();
        var inFlight = Guid.NewGuid();
        await hub.Clients.Group(group).SendAsync("TableStatusChanged", inFlight.ToString(), "Occupied");
        await Task.WhenAll(probe.EventOrCloseAsync(inFlight), director.EventOrCloseAsync(inFlight));
        probe.Saw(inFlight).Should().BeFalse("a suspended company receives nothing");
        director.Saw(inFlight).Should().BeFalse("a suspended company receives nothing");
        await probe.ClosedAsync();
        await director.ClosedAsync();

        var otherEvent = await _hub.EmitEventAsync(other);
        await otherProbe.SawAsync(otherEvent);
        otherProbe.IsClosed.Should().BeFalse("another company has nothing to do with it");

        await platform.PutAsJsonAsync($"/api/platform/tenants/{company.TenantId}/status", new { status = "Active" })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await using var back = await _hub.ListenAsync(company, company.DirectorToken);
        await _hub.BothReceiveAsync(company, back);
    }

    [Fact]
    public async Task Leaving_a_platform_access_closes_its_connection_and_not_the_one_of_the_person()
    {
        var company = await _hub.NewCompanyAsync();
        await using var person = await _hub.ListenAsync(company, company.DirectorToken);
        var platform = await _sessions.PlatformClientAsync();
        var granted = await platform.PostAsJsonAsync($"/api/platform/tenants/{company.TenantId}/impersonate", new { reason = "Suporte: conferir a cozinha" });
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var token = (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        await using var access = await _hub.ListenAsync(company, token);
        await _hub.BothReceiveAsync(company, access, person);

        await _sessions.ClientWith(token).PostAsync("/api/auth/impersonation/end", null).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        await _hub.ShouldNoLongerReceiveAsync(company, access, person);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // TOKEN EXPIRY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_connection_closes_when_the_token_expires_and_is_told_it_may_reconnect()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;
        await using var raw = await RawHubClient.ConnectAsync(_hub.Server, _factory.CreateApiClient(), HubKit.WithLifetime(token, TimeSpan.FromSeconds(3)));

        await raw.ServerEndedTheSocketAsync();

        var close = JsonDocument.Parse(raw.Frames.Single(frame => frame.Contains("\"type\":7", StringComparison.Ordinal))).RootElement;
        close.GetProperty("allowReconnect").GetBoolean().Should().BeTrue("an expired token is not a revoked session: the client renews it and comes back");
    }

    [Fact]
    public async Task A_client_with_a_renewed_token_comes_back_by_itself_once_and_keeps_receiving()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;
        var issued = 0;
        var tokens = new Func<Task<string?>>(() =>
            // the first token lasts seconds; the next one, the normal lifetime
            Task.FromResult<string?>(Interlocked.Increment(ref issued) <= 2 ? HubKit.WithLifetime(token, TimeSpan.FromSeconds(3)) : token));
        await using var probe = await HubProbe.ConnectAsync(_hub.Server, token, renewedToken: tokens);
        await probe.JoinAsync(company.StoreId);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);

        await probe.ReconnectingAsync();
        await probe.ReconnectedAsync();
        await probe.JoinAsync(company.StoreId);

        await _hub.BothReceiveAsync(company, probe, witness);
        issued.Should().BeLessThan(6, "one negotiation and one socket per reconnect, not a loop");
    }

    [Fact]
    public async Task A_connection_of_a_revoked_session_does_not_try_to_reconnect()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var token = (await _hub.SignInAsync(target)).AccessToken;
        await using var probe = await HubProbe.ConnectAsync(_hub.Server, token, renewedToken: () => Task.FromResult<string?>(token));
        await probe.JoinAsync(company.StoreId);

        await UpdateAsync(company, target, "gerente");

        await probe.ClosedAsync();
        probe.WentReconnecting.Should().BeFalse("the server closed it for good");
    }

    [Fact]
    public async Task A_refresh_keeps_the_open_connection_because_the_stamp_is_the_same()
    {
        var company = await _hub.NewCompanyAsync();
        var target = await _hub.AddPersonAsync(company, UserRole.Vendedor);
        var login = await _hub.SignInAsync(target);
        await using var probe = await _hub.ListenAsync(company, login.AccessToken);
        await using var witness = await _hub.ListenAsync(company, company.DirectorToken);

        // a refresh issues a new token with the same stamp: the open connection is still the same valid session
        await _sessions.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await _hub.BothReceiveAsync(company, probe, witness);
        await probe.LeaveAsync(company.StoreId);
        await probe.JoinAsync(company.StoreId);
        await _hub.BothReceiveAsync(company, probe, witness);
    }

    [Fact]
    public async Task Long_polling_can_join_a_store_at_all()
    {
        var company = await _hub.NewCompanyAsync();
        await using var probe = await _hub.ListenAsync(company, company.DirectorToken, HubTransport.LongPolling);

        await _hub.BothReceiveAsync(company, probe);
    }
}
