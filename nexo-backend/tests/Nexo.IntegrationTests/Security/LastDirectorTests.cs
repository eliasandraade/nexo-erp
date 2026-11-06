using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit.Abstractions;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// A company never ends up without an active Diretoria through anything the system does: nobody else could administer it.
/// Every test creates its own company and goes through the real API or the real DbContext, against a real PostgreSQL.
/// Every other company of the database (the seed included) has directors, so a count that forgets the tenant fails here.
/// </summary>
[Collection("Integration")]
public sealed partial class LastDirectorTests
{
    private const string UsersRoute = "/api/users";
    private const string CompanyRoute = "/api/categories";
    private const string AnyModule = "varejo";
    private const string RefusalCode = "last_director";
    private const string RefusalMessage =
        "A empresa precisa ter pelo menos um usuário ativo na Diretoria. Promova ou reative outra pessoa na Diretoria antes de fazer esta alteração.";

    /// <summary>Rounds of each race, each one in a new company: enough to hit the interleavings the scheduler offers.</summary>
    private const int Rounds = 25;

    /// <summary>
    /// Work factor 4: the real hasher verifies any factor, so signing in stays real and a round spends its time on the race,
    /// not on BCrypt.
    /// </summary>
    private static readonly string CheapPasswordHash = BCrypt.Net.BCrypt.HashPassword(TestCredentials.SameTenantManagerPassword, 4);

    private readonly TestWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public LastDirectorTests(TestWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFUSED
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("vendedor", "active")]
    [InlineData("estoquista", "active")]
    [InlineData("cozinha", "active")]
    [InlineData("Gerente", "active")]
    [InlineData("diretoria", "inactive")]
    [InlineData("diretoria", "blocked")]
    [InlineData("Diretoria", "BLOCKED")]
    [InlineData("gerente", "blocked")]
    public async Task The_last_active_director_cannot_leave_and_nothing_changes(string role, string status)
    {
        var director = await NewCompanyAsync();
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, director.Login, director.Password);
        var before = await SnapshotOfAsync(director.UserId);

        using var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, role, status, fullName: "Nome Que Nao Fica"));

        await ShouldBeRefusedAsync(refused);
        (await SnapshotOfAsync(director.UserId)).Should().Be(before, "a refused change writes nothing, the name of the same request included");
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
        (await LiveSessionsAsync(director.UserId)).Should().Be(1, "a refused change revokes nothing");
        (await AuditedUpdatesOfAsync(director.UserId)).Should().Be(0, "nothing happened to audit");
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory, "the token still works");
        await RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory, "the refresh token still works");
    }

    [Theory]
    [InlineData(UserStatus.PendingVerification)]
    [InlineData(UserStatus.Blocked)]
    [InlineData(UserStatus.Inactive)]
    public async Task A_director_who_is_not_active_does_not_count(UserStatus status)
    {
        var director = await NewCompanyAsync();
        await AddUserAsync(director, UserRole.Diretoria, status);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        using var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active"));

        await ShouldBeRefusedAsync(refused);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task A_director_of_another_company_cannot_be_reached()
    {
        var director = await NewCompanyAsync();
        var stranger = await NewCompanyAsync();
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        await client.PutAsJsonAsync(RouteOf(stranger), Body(stranger, "gerente", "active"))
            .ShouldHaveStatusAsync(HttpStatusCode.NotFound, _factory);

        (await ActiveDirectorsAsync(stranger.TenantId)).Should().Be(1);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // KEEPS WORKING
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    [InlineData("diretoria", "inactive")]
    public async Task Changing_a_director_while_another_stays_works_and_ends_their_sessions(string role, string status)
    {
        var acting = await NewCompanyAsync();
        var target = await AddUserAsync(acting, UserRole.Diretoria);
        var actingClient = await AuthClientFactory.LoginAsync(_factory, acting.Login, acting.Password);
        var (targetClient, targetLogin) = await AuthClientFactory.LoginWithBodyAsync(_factory, target.Login, target.Password);
        var stampBefore = (await SnapshotOfAsync(target.UserId)).Stamp;

        await actingClient.PutAsJsonAsync(RouteOf(target), Body(target, role, status)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await SnapshotOfAsync(target.UserId)).Stamp.Should().NotBe(stampBefore, "issue #137: any role change or block bumps the stamp");
        (await LiveSessionsAsync(target.UserId)).Should().Be(0, "issue #137: every session ends");
        await targetClient.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await RefreshAsync(targetLogin.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await actingClient.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(acting.TenantId)).Should().Be(1);
    }

    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    [InlineData("diretoria", "inactive")]
    public async Task A_director_can_leave_while_another_stays_and_the_one_left_cannot(string role, string status)
    {
        var leaving = await NewCompanyAsync();
        var staying = await AddUserAsync(leaving, UserRole.Diretoria);
        var leavingClient = await AuthClientFactory.LoginAsync(_factory, leaving.Login, leaving.Password);
        var stayingClient = await AuthClientFactory.LoginAsync(_factory, staying.Login, staying.Password);

        await leavingClient.PutAsJsonAsync(RouteOf(leaving), Body(leaving, role, status)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await leavingClient.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        using var refused = await stayingClient.PutAsJsonAsync(RouteOf(staying), Body(staying, role, status));
        await ShouldBeRefusedAsync(refused);
        (await ActiveDirectorsAsync(leaving.TenantId)).Should().Be(1);
    }

    [Theory]
    [InlineData(UserRole.Vendedor)]
    [InlineData(UserRole.Gerente)]
    public async Task Promoting_anyone_works_and_the_new_director_counts(UserRole from)
    {
        var director = await NewCompanyAsync();
        var promoted = await AddUserAsync(director, from);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        await client.PutAsJsonAsync(RouteOf(promoted), Body(promoted, "diretoria", "active")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(2);

        await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    [Theory]
    [InlineData(UserStatus.Blocked)]
    [InlineData(UserStatus.Inactive)]
    public async Task Reactivating_a_director_works_and_they_count_again(UserStatus status)
    {
        var director = await NewCompanyAsync();
        var away = await AddUserAsync(director, UserRole.Diretoria, status);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        await client.PutAsJsonAsync(RouteOf(away), Body(away, "diretoria", "active")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(2);

        await client.PutAsJsonAsync(RouteOf(director), Body(director, "diretoria", "blocked")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    [Theory]
    [InlineData("diretoria", "active")]
    [InlineData("Diretoria", "ACTIVE")]
    [InlineData("diretoria", null)]
    public async Task Editing_the_last_director_without_leaving_works_and_keeps_the_session(string role, string? status)
    {
        var director = await NewCompanyAsync();
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, director.Login, director.Password);
        var stampBefore = (await SnapshotOfAsync(director.UserId)).Stamp;
        var newEmail = $"novo-{Guid.NewGuid():N}"[..16] + $"@{TestCredentials.TestDomain}";

        await client.PutAsJsonAsync(RouteOf(director), new { fullName = "Nome Novo", email = newEmail, role, phone = "11999990000", notes = "Nota", status })
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        var after = await SnapshotOfAsync(director.UserId);
        after.FullName.Should().Be("Nome Novo");
        after.Email.Should().Be(newEmail);
        after.Role.Should().Be(UserRole.Diretoria);
        after.Status.Should().Be(UserStatus.Active);
        after.Stamp.Should().Be(stampBefore, "issue #137: an edit without a role or status change keeps the stamp");
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Theory]
    [InlineData(UserStatus.Blocked, "gerente", "blocked")]
    [InlineData(UserStatus.Blocked, "diretoria", "inactive")]
    [InlineData(UserStatus.Inactive, "vendedor", "inactive")]
    [InlineData(UserStatus.PendingVerification, "gerente", null)]
    [InlineData(UserStatus.PendingVerification, "gerente", "pendingverification")]
    [InlineData(UserStatus.PendingVerification, "diretoria", "blocked")]
    public async Task A_director_who_is_not_active_can_still_be_changed(UserStatus current, string role, string? status)
    {
        var director = await NewCompanyAsync();
        var away = await AddUserAsync(director, UserRole.Diretoria, current);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        await client.PutAsJsonAsync(RouteOf(away), Body(away, role, status)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    public enum Activation { ResetLink, VerificationLink }

    /// <summary>Only the e-mail links activate someone who is pending: an edit cannot (see PendingUserEditTests).</summary>
    [Theory]
    [InlineData(Activation.ResetLink)]
    [InlineData(Activation.VerificationLink)]
    public async Task A_pending_director_starts_to_count_once_activated(Activation how)
    {
        var director = await NewCompanyAsync();
        var pending = await AddUserAsync(director, UserRole.Diretoria, UserStatus.PendingVerification);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        using (var refused = await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")))
            await ShouldBeRefusedAsync(refused);

        switch (how)
        {
            case Activation.ResetLink:
                var resetToken = $"token-{Guid.NewGuid():N}";
                await ChangeUserAsync(pending.UserId, u => u.SetPasswordResetToken(HashOf(resetToken), DateTime.UtcNow.AddMinutes(30)));
                await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/reset-password", new { token = resetToken, newPassword = $"Nova!{Guid.NewGuid():N}" })
                    .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
                break;
            case Activation.VerificationLink:
                var verificationToken = Guid.NewGuid().ToString("N");
                await ChangeUserAsync(pending.UserId, u => u.SetVerificationToken(verificationToken, DateTime.UtcNow.AddMinutes(30)));
                await _factory.CreateApiClient().GetAsync($"/api/auth/verify-email?token={verificationToken}")
                    .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
                break;
        }
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(2);

        await client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // A COMPANY ALREADY WITHOUT A DIRECTOR
    // ────────────────────────────────────────────────────────────────────────────

    public enum Repair { RenameColleague, PromoteColleague, ReactivateSelf, MoveAnAwayDirector, DemoteSelfStillBlocked }

    /// <summary>
    /// The rule refuses what removes a director, not what happens to a company that has none: otherwise the state could
    /// never be repaired. The acting token is one that was already out when the company reached zero (the status is cached
    /// and the row is changed without bumping the stamp, the way a manual fix in the database would).
    /// </summary>
    [Theory]
    [InlineData(Repair.RenameColleague, 0)]
    [InlineData(Repair.PromoteColleague, 1)]
    [InlineData(Repair.ReactivateSelf, 1)]
    [InlineData(Repair.MoveAnAwayDirector, 0)]
    [InlineData(Repair.DemoteSelfStillBlocked, 0)]
    public async Task A_company_already_without_a_director_can_still_be_repaired(Repair what, int activeDirectorsAfter)
    {
        var acting = await NewCompanyAsync();
        var colleague = await AddUserAsync(acting, UserRole.Gerente);
        var away = await AddUserAsync(acting, UserRole.Diretoria, UserStatus.Blocked);
        var client = await AuthClientFactory.LoginAsync(_factory, acting.Login, acting.Password);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NexoDbContext>().Users.IgnoreQueryFilters()
                .Where(u => u.Id == acting.UserId)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, UserStatus.Blocked));
        }
        (await ActiveDirectorsAsync(acting.TenantId)).Should().Be(0);

        var change = what switch
        {
            Repair.RenameColleague => client.PutAsJsonAsync(RouteOf(colleague), Body(colleague, "gerente", "active", fullName: "Outro Nome")),
            Repair.PromoteColleague => client.PutAsJsonAsync(RouteOf(colleague), Body(colleague, "diretoria", "active")),
            Repair.ReactivateSelf => client.PutAsJsonAsync(RouteOf(acting), Body(acting, "diretoria", "active")),
            Repair.MoveAnAwayDirector => client.PutAsJsonAsync(RouteOf(away), Body(away, "gerente", "inactive")),
            Repair.DemoteSelfStillBlocked => client.PutAsJsonAsync(RouteOf(acting), Body(acting, "gerente", "blocked")),
            _ => throw new ArgumentOutOfRangeException(nameof(what), what, null),
        };

        await change.ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await ActiveDirectorsAsync(acting.TenantId)).Should().Be(activeDirectorsAfter);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // THE DATA CONTEXT: NO PATH SKIPS THE RULE, A FUTURE ONE INCLUDED
    // ────────────────────────────────────────────────────────────────────────────

    public enum Removal { Demotion, Block, Inactivation, Deletion }

    private static void Apply(NexoDbContext db, User user, Removal how)
    {
        switch (how)
        {
            case Removal.Demotion: user.UpdateProfile(user.FullName, user.Email, user.Phone, UserRole.Gerente, user.Notes); break;
            case Removal.Block: user.SetStatus(UserStatus.Blocked); break;
            case Removal.Inactivation: user.SetStatus(UserStatus.Inactive); break;
            case Removal.Deletion: db.Users.Remove(user); break;
        }
    }

    [Theory]
    [InlineData(Removal.Demotion)]
    [InlineData(Removal.Block)]
    [InlineData(Removal.Inactivation)]
    [InlineData(Removal.Deletion)]
    public async Task The_context_refuses_a_save_that_removes_the_last_director(Removal how)
    {
        var director = await NewCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == director.UserId), how);

        var save = () => db.SaveChangesAsync();

        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(RefusalCode);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    [Theory]
    [InlineData(Removal.Demotion)]
    [InlineData(Removal.Deletion)]
    public async Task The_context_refuses_it_inside_a_transaction_the_caller_opened(Removal how)
    {
        var director = await NewCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == director.UserId), how);

        var save = () => db.SaveChangesAsync();

        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(RefusalCode);
        await tx.RollbackAsync();
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    [Theory]
    [InlineData(Removal.Demotion)]
    [InlineData(Removal.Block)]
    [InlineData(Removal.Inactivation)]
    [InlineData(Removal.Deletion)]
    public async Task The_context_allows_it_when_another_director_stays(Removal how)
    {
        var director = await NewCompanyAsync();
        var other = await AddUserAsync(director, UserRole.Diretoria);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == other.UserId), how);

        await db.SaveChangesAsync();

        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task The_context_allows_handing_over_in_a_single_save()
    {
        var director = await NewCompanyAsync();
        var heir = await AddUserAsync(director, UserRole.Vendedor);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Apply(db, await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == director.UserId), Removal.Demotion);
        var promoted = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == heir.UserId);
        promoted.UpdateProfile(promoted.FullName, promoted.Email, promoted.Phone, UserRole.Diretoria, promoted.Notes);

        await db.SaveChangesAsync();

        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
        (await SnapshotOfAsync(heir.UserId)).Role.Should().Be(UserRole.Diretoria);
    }

    /// <summary>
    /// What this request read before the save is stale: the director was blocked then, and meanwhile came back and became
    /// the only one. Deciding on the loaded values would let the save through.
    /// </summary>
    [Fact]
    public async Task The_context_decides_on_what_is_committed_not_on_what_was_read()
    {
        var director = await NewCompanyAsync();
        var away = await AddUserAsync(director, UserRole.Diretoria, UserStatus.Blocked);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var stale = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == away.UserId);

        await ChangeUserAsync(away.UserId, u => u.SetStatus(UserStatus.Active));
        await ChangeUserAsync(director.UserId, u => u.SetStatus(UserStatus.Inactive));
        stale.UpdateProfile(stale.FullName, stale.Email, stale.Phone, UserRole.Gerente, stale.Notes);

        var save = () => db.SaveChangesAsync();

        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(RefusalCode);
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    /// <summary>The rule needs an async wait for the lock, so a synchronous save is not allowed to touch who is a director at all.</summary>
    [Fact]
    public async Task A_synchronous_save_cannot_go_around_the_rule()
    {
        var director = await NewCompanyAsync();
        var other = await AddUserAsync(director, UserRole.Diretoria);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == other.UserId);

        user.UpdateProfile("Nome Novo", user.Email, user.Phone, UserRole.Diretoria, user.Notes);
        db.SaveChanges();
        (await SnapshotOfAsync(other.UserId)).FullName.Should().Be("Nome Novo", "a save that does not touch who is a director goes on as usual");

        user.SetStatus(UserStatus.Blocked);
        var save = () => db.SaveChanges();

        save.Should().Throw<InvalidOperationException>();
        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(2);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // OTHER PATHS THAT TOUCH A USER
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task What_the_platform_can_do_to_a_user_does_not_change_who_is_a_director()
    {
        var director = await NewCompanyAsync();
        var platform = await AuthClientFactory.LoginAsPlatformAsync(_factory);
        var route = $"/api/platform/tenants/{director.TenantId}/users/{director.UserId}";

        await platform.PostAsJsonAsync($"{route}/reset-password", new { newPassword = $"Nova!{Guid.NewGuid():N}" }).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await platform.PostAsync($"{route}/force-logout", null).ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await platform.DeleteAsync($"{route}/sessions").ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        (await ActiveDirectorsAsync(director.TenantId)).Should().Be(1);
    }

    /// <summary>Nothing deletes a user today. A new route with that purpose has to come through here and through the rule.</summary>
    [Fact]
    public void No_route_deletes_a_user()
    {
        var deletes = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("DELETE") == true)
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(r => r.Contains("user", StringComparison.OrdinalIgnoreCase));

        deletes.Should().BeEquivalentTo(["api/platform/tenants/{tenantId:guid}/users/{userId:guid}/sessions"], "this one ends sessions, it does not delete the user");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CONCURRENCY: DIFFERENT CONNECTIONS, REAL POSTGRESQL
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two directors act on each other at the same time. The one who lost either waited for the lock and got the refusal,
    /// or arrived after the other committed and its session had already been ended (401). Never a 500, never both.
    /// </summary>
    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    [InlineData("diretoria", "inactive")]
    [InlineData("vendedor", "blocked")]
    public async Task Two_directors_acting_on_each_other_at_once_never_leave_the_company_empty(string role, string status)
    {
        var refusedByTheRule = 0;
        var sessionAlreadyEnded = 0;
        for (var round = 0; round < Rounds; round++)
        {
            var directors = await NewDirectorsAsync(2);
            var (first, firstClient) = directors[0];
            var (second, secondClient) = directors[1];

            var responses = await RaceAsync(
                () => firstClient.PutAsJsonAsync(RouteOf(second), Body(second, role, status)),
                () => secondClient.PutAsJsonAsync(RouteOf(first), Body(first, role, status)));

            var left = await ActiveDirectorsAsync(first.TenantId);
            left.Should().BeGreaterThanOrEqualTo(1, $"round {round}: {await DescribeAsync(responses)}");
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, $"round {round}: {await DescribeAsync(responses)}");
            var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
            if (loser.StatusCode == HttpStatusCode.Conflict)
            {
                await ShouldBeRefusedAsync(loser);
                refusedByTheRule++;
            }
            else
            {
                loser.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"round {round}: {await DescribeAsync(responses)}");
                sessionAlreadyEnded++;
            }
            foreach (var response in responses) response.Dispose();
        }

        _output.WriteLine($"{Rounds} rounds: {refusedByTheRule} refused by the rule (409), {sessionAlreadyEnded} with the session already ended (401).");
        refusedByTheRule.Should().BeGreaterThan(0, "otherwise no round really raced");
    }

    /// <summary>Each one leaves on their own: no session of the loser is touched, so the only possible answer is the refusal.</summary>
    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    [InlineData("diretoria", "inactive")]
    public async Task Two_directors_leaving_at_once_leave_exactly_one(string role, string status)
    {
        for (var round = 0; round < Rounds; round++)
        {
            var directors = await NewDirectorsAsync(2);
            var (first, firstClient) = directors[0];
            var (second, secondClient) = directors[1];

            var responses = await RaceAsync(
                () => firstClient.PutAsJsonAsync(RouteOf(first), Body(first, role, status)),
                () => secondClient.PutAsJsonAsync(RouteOf(second), Body(second, role, status)));

            (await ActiveDirectorsAsync(first.TenantId)).Should().Be(1, $"round {round}: {await DescribeAsync(responses)}");
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, $"round {round}: {await DescribeAsync(responses)}");
            await ShouldBeRefusedAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK));
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Many_directors_leaving_at_once_leave_exactly_one()
    {
        const int directorsInTheCompany = 5;
        for (var round = 0; round < Rounds / 2; round++)
        {
            var directors = await NewDirectorsAsync(directorsInTheCompany);

            var responses = await RaceAsync(directors
                .Select<(SyntheticAccount Account, HttpClient Client), Func<Task<HttpResponseMessage>>>(d =>
                    () => d.Client.PutAsJsonAsync(RouteOf(d.Account), Body(d.Account, "gerente", "active")))
                .ToArray());

            (await ActiveDirectorsAsync(directors[0].Account.TenantId)).Should().Be(1, $"round {round}: {await DescribeAsync(responses)}");
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(directorsInTheCompany - 1, $"round {round}: {await DescribeAsync(responses)}");
            await ShouldBeRefusedAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK));
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task A_promotion_racing_with_the_last_director_leaving_never_leaves_the_company_empty()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (director, client) = (await NewDirectorsAsync(1))[0];
            var colleague = await AddUserAsync(director, UserRole.Gerente);

            var responses = await RaceAsync(
                () => client.PutAsJsonAsync(RouteOf(colleague), Body(colleague, "diretoria", "active")),
                () => client.PutAsJsonAsync(RouteOf(director), Body(director, "gerente", "active")));

            (await ActiveDirectorsAsync(director.TenantId)).Should().BeGreaterThanOrEqualTo(1, $"round {round}: {await DescribeAsync(responses)}");
            responses[0].StatusCode.Should().Be(HttpStatusCode.OK, $"round {round}: {await DescribeAsync(responses)}");
            if (responses[1].StatusCode != HttpStatusCode.OK) await ShouldBeRefusedAsync(responses[1]);
            foreach (var response in responses) response.Dispose();
        }
    }

    /// <summary>
    /// The race, deterministically. The first request stops after reading its target; the second one starts and passes the
    /// session check while nothing is committed, and has to wait for the first one before reading anything. A read-then-write
    /// check would let the second one through here, every time.
    /// </summary>
    [Fact]
    public async Task A_request_that_waited_for_the_other_decides_on_what_the_other_committed()
    {
        var directors = await NewDirectorsAsync(2);
        var (first, firstClient) = directors[0];
        var (second, secondClient) = directors[1];

        using var hold = _factory.Gate.HoldNext(DbCommandGate.UserRowOf(second.UserId), DbCommandGate.Moment.AfterRunning);
        var firstRequest = firstClient.PutAsJsonAsync(RouteOf(second), Body(second, "gerente", "active"));
        await hold.ReachedAsync();

        var secondRequest = secondClient.PutAsJsonAsync(RouteOf(first), Body(first, "gerente", "active"));
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!secondRequest.IsCompleted && !await SomeoneWaitsForTheLockAsync(first.TenantId))
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("The second request neither finished nor waited for the lock.");
            await Task.Delay(20);
        }

        hold.Release();
        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(firstResponse));
        await ShouldBeRefusedAsync(secondResponse);
        (await ActiveDirectorsAsync(first.TenantId)).Should().Be(1);
        (await SnapshotOfAsync(first.UserId)).Role.Should().Be(UserRole.Diretoria, "the refused request changed nothing");
    }

    /// <summary>The same race one level down, for a path that never heard of the rule: only the context stands between it and zero.</summary>
    [Fact]
    public async Task Two_saves_on_different_connections_never_both_remove_a_director()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var directors = await NewDirectorsAsync(2);
            var first = directors[0].Account;
            var second = directors[1].Account;
            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstDb = firstScope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var secondDb = secondScope.ServiceProvider.GetRequiredService<NexoDbContext>();
            Apply(firstDb, await firstDb.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == first.UserId), Removal.Block);
            Apply(secondDb, await secondDb.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == second.UserId), Removal.Demotion);

            var failures = await Task.WhenAll(TrySaveAsync(firstDb), TrySaveAsync(secondDb));

            (await ActiveDirectorsAsync(first.TenantId)).Should().Be(1, $"round {round}");
            failures.Count(f => f is null).Should().Be(1, $"round {round}");
            failures.Single(f => f is not null).Should().BeAssignableTo<ConflictException>().Which.Code.Should().Be(RefusalCode);
        }
    }

    /// <summary>A stalled change does not hang whoever comes next, nor turns into a 500: a conflict of its own, to try again.</summary>
    [Fact]
    public async Task When_the_wait_for_the_lock_runs_out_the_answer_is_a_conflict_of_its_own()
    {
        var director = await NewCompanyAsync();
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var before = await SnapshotOfAsync(director.UserId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await using var stalled = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({LockNameOf(director.TenantId)}))");

        using var response = await client.PutAsJsonAsync(RouteOf(director), Body(director, "diretoria", "active", fullName: "Nome Que Nao Fica"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("users_busy");
        await stalled.RollbackAsync();
        (await SnapshotOfAsync(director.UserId)).Should().Be(before);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    private sealed record Snapshot(UserRole Role, UserStatus Status, string Stamp, string FullName, string Email);

    private static string RouteOf(SyntheticAccount user) => $"{UsersRoute}/{user.UserId}";

    private static string LockNameOf(Guid tenantId) => $"users:directors:{tenantId}";

    private static string HashOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static object Body(SyntheticAccount user, string role, string? status, string? fullName = null)
        => new { fullName = fullName ?? user.FullName, email = user.Email, role, phone = (string?)null, notes = (string?)null, status };

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(refreshToken));

    private async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await _factory.ServerLog.DescribeAsync(response));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(RefusalCode);
        body.GetProperty("error").GetString().Should().Be(RefusalMessage);
        body.GetProperty("details").GetArrayLength().Should().Be(0);
        body.GetProperty("requestId").GetString().Should().NotBeNullOrEmpty();
    }

    private async Task<string> DescribeAsync(IEnumerable<HttpResponseMessage> responses)
    {
        var described = new List<string>();
        foreach (var response in responses) described.Add(await _factory.ServerLog.DescribeAsync(response));
        return string.Join(Environment.NewLine, described);
    }

    private async Task<T> ReadAsync<T>(Func<NexoDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<NexoDbContext>());
    }

    private Task<int> ActiveDirectorsAsync(Guid tenantId)
        => ReadAsync(db => db.Users.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == tenantId && u.Role == UserRole.Diretoria && u.Status == UserStatus.Active));

    private Task<Snapshot> SnapshotOfAsync(Guid userId)
        => ReadAsync(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .Select(u => new Snapshot(u.Role, u.Status, u.SecurityStamp, u.FullName, u.Email)).SingleAsync());

    private Task<int> LiveSessionsAsync(Guid userId)
        => ReadAsync(db => db.UserSessions.IgnoreQueryFilters().CountAsync(s => s.UserId == userId && !s.IsRevoked));

    private Task<int> AuditedUpdatesOfAsync(Guid userId)
    {
        var entityId = userId.ToString();
        return ReadAsync(db => db.AuditRecords.IgnoreQueryFilters().CountAsync(a => a.ActionType == AuditActions.UserUpdated && a.EntityId == entityId));
    }

    private Task<bool> SomeoneWaitsForTheLockAsync(Guid tenantId)
        => ReadAsync(async db => (await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted AND objid = (hashtext({LockNameOf(tenantId)})::bigint & 4294967295)::oid
            """).ToListAsync())[0] > 0);

    private async Task ChangeUserAsync(Guid userId, Action<User> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        change(await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId));
        await db.SaveChangesAsync();
    }

    private static async Task<Exception?> TrySaveAsync(NexoDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Starts every request at the same moment, each on its own thread and, so, on its own connection.</summary>
    private static async Task<HttpResponseMessage[]> RaceAsync(params Func<Task<HttpResponseMessage>>[] requests)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = requests.Select(request => Task.Run(async () =>
        {
            await start.Task;
            return await request();
        })).ToArray();
        start.SetResult();
        return await Task.WhenAll(running);
    }

    /// <summary>A new company with these directors, all signed in.</summary>
    private async Task<(SyntheticAccount Account, HttpClient Client)[]> NewDirectorsAsync(int count)
    {
        var first = await NewCompanyAsync();
        var accounts = new List<SyntheticAccount> { first };
        for (var i = 1; i < count; i++) accounts.Add(await AddUserAsync(first, UserRole.Diretoria));
        var clients = await Task.WhenAll(accounts.Select(a => AuthClientFactory.LoginAsync(_factory, a.Login, a.Password)));
        return accounts.Zip(clients).ToArray();
    }

    /// <summary>A new company, with a store and a module with access, and its active director.</summary>
    private async Task<SyntheticAccount> NewCompanyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tag = NewTag();
        var taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var tenant = Tenant.Create($"Sintetica {tag}", taxId, $"sintetica-{tag}@{TestCredentials.TestDomain}");
        db.Tenants.Add(tenant);
        var store = Store.Create(tenant.Id, $"Loja {tag}", $"loja-{tag}");
        db.Stores.Add(store);
        db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(tenant.Id, AnyModule));
        var director = NewUser(db, tenant.Id, store.Id, UserRole.Diretoria, UserStatus.Active);
        await db.SaveChangesAsync();
        return director;
    }

    private async Task<SyntheticAccount> AddUserAsync(SyntheticAccount colleague, UserRole role, UserStatus status = UserStatus.Active)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = NewUser(db, colleague.TenantId, colleague.StoreId, role, status);
        await db.SaveChangesAsync();
        return user;
    }

    private static SyntheticAccount NewUser(NexoDbContext db, Guid tenantId, Guid storeId, UserRole role, UserStatus status)
    {
        var login = $"sint-{NewTag()}";
        var email = $"{login}@{TestCredentials.TestDomain}";
        const string fullName = "Pessoa Sintetica";
        var user = User.Create(tenantId, fullName, email, login, CheapPasswordHash, role);
        user.SetStatus(status);
        db.Users.Add(user);
        return new SyntheticAccount(tenantId, storeId, user.Id, login, email, fullName, TestCredentials.SameTenantManagerPassword);
    }

    private static string NewTag() => Guid.NewGuid().ToString("N")[..12];
}
