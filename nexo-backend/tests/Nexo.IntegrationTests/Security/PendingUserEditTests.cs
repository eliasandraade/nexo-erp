using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// What editing a user does to the status when the address was never proven. The e-mail link is the only way from unverified to
/// active: the edit saves the rest of the form, can stop the person, and never lets them in. See docs/security/rbac-matrix.md.
/// </summary>
[Collection("Integration")]
public sealed class PendingUserEditTests
{
    private const string UsersRoute = "/api/users";
    private const string AnyModule = "varejo";
    private const string NotVerifiedCode = "email_not_verified";
    private const string NotEditableCode = "status_not_editable";
    private const string PendingOnTheApi = "pendingverification";

    private readonly TestWebApplicationFactory _factory;

    public PendingUserEditTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── The reported failure ──────────────────────────────────────────────────

    /// <summary>The users screen sends back the status it read: this request answered 400 and no pending user could be edited.</summary>
    [Fact]
    public async Task What_the_api_returned_for_a_pending_user_can_be_saved_back()
    {
        var (client, _) = await DirectorAsync();
        var (pending, _) = await PendingUserAsync(client.Director);
        var read = await client.Http.GetFromJsonAsync<JsonElement>(RouteOf(pending));
        read.GetProperty("status").GetString().Should().Be(PendingOnTheApi);

        using var saved = await client.Http.PutAsJsonAsync(RouteOf(pending), new
        {
            fullName = "Nome Corrigido", email = read.GetProperty("email").GetString(), role = read.GetProperty("role").GetString(),
            phone = (string?)null, notes = (string?)null, status = read.GetProperty("status").GetString(),
        });

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(saved));
        (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be(PendingOnTheApi);
        (await RowAsync(pending)).FullName.Should().Be("Nome Corrigido");
    }

    // ── Pending stays pending ─────────────────────────────────────────────────

    [Theory]
    [InlineData(PendingOnTheApi)]
    [InlineData("PendingVerification")]
    [InlineData(null)]
    public async Task An_edit_that_says_nothing_new_keeps_the_user_pending_and_the_link_they_got_working(string? status)
    {
        var (client, _) = await DirectorAsync();
        var (pending, link) = await PendingUserAsync(client.Director);
        var stampBefore = (await RowAsync(pending)).SecurityStamp;

        await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, status, fullName: "Nome Novo")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        var after = await RowAsync(pending);
        after.Status.Should().Be(UserStatus.PendingVerification);
        after.FullName.Should().Be("Nome Novo");
        after.SecurityStamp.Should().Be(stampBefore);
        await Verify(link).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await RowAsync(pending)).Status.Should().Be(UserStatus.Active);
    }

    [Theory]
    [InlineData("active", UserRole.Vendedor)]
    [InlineData("ACTIVE", UserRole.Vendedor)]
    [InlineData("active", UserRole.Diretoria)]
    public async Task An_edit_cannot_activate_a_pending_user_and_changes_nothing(string status, UserRole role)
    {
        var (client, _) = await DirectorAsync();
        var (pending, _) = await PendingUserAsync(client.Director, role);
        var before = await RowAsync(pending);

        using var refused = await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, status, role: role.ToString(), fullName: "Nome Que Nao Fica"));

        await ShouldBeRefusedAsync(refused, NotVerifiedCode);
        var after = await RowAsync(pending);
        after.Status.Should().Be(UserStatus.PendingVerification);
        after.FullName.Should().Be(before.FullName);
        (await LoginStatusAsync(pending)).Should().NotBe(HttpStatusCode.OK, "the e-mail was not proven");
    }

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Inactive)]
    [InlineData(UserStatus.Blocked)]
    public async Task An_edit_cannot_send_back_to_pending_someone_whose_address_is_proven(UserStatus current)
    {
        var (client, _) = await DirectorAsync();
        var colleague = await SyntheticTenant.AddUserAsync(_factory, client.Director, UserRole.Vendedor, current);

        using var refused = await client.Http.PutAsJsonAsync(RouteOf(colleague), Body(colleague, PendingOnTheApi, fullName: "Nome Que Nao Fica"));

        await ShouldBeRefusedAsync(refused, NotEditableCode);
        var after = await RowAsync(colleague);
        after.Status.Should().Be(current);
        after.FullName.Should().Be(colleague.FullName);
    }

    [Fact]
    public async Task The_last_director_cannot_leave_by_being_sent_back_to_pending()
    {
        var (client, director) = await DirectorAsync();

        using var refused = await client.Http.PutAsJsonAsync(RouteOf(director), Body(director, PendingOnTheApi, role: "diretoria"));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RowAsync(director)).Status.Should().Be(UserStatus.Active);
    }

    // ── Stopping someone who never verified, and the way back ─────────────────

    [Theory]
    [InlineData("inactive", UserStatus.Inactive)]
    [InlineData("blocked", UserStatus.Blocked)]
    public async Task A_pending_user_can_be_stopped_and_no_link_lets_them_in_after_that(string status, UserStatus expected)
    {
        var (client, _) = await DirectorAsync();
        var (pending, link) = await PendingUserAsync(client.Director);
        var resetToken = $"token-{Guid.NewGuid():N}";
        await ChangeAsync(pending, u => u.SetPasswordResetToken(HashOf(resetToken), DateTime.UtcNow.AddMinutes(30)));

        await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, status)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await RowAsync(pending)).Status.Should().Be(expected);
        (await Verify(link)).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/reset-password", new { token = resetToken, newPassword = $"Nova!{Guid.NewGuid():N}" }))
            .StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await RowAsync(pending)).Status.Should().Be(expected);
    }

    /// <summary>Stopping and reactivating would otherwise be a detour around the link, in two clicks of the same screen.</summary>
    [Theory]
    [InlineData("inactive", "active")]
    [InlineData("blocked", "active")]
    [InlineData("inactive", PendingOnTheApi)]
    [InlineData("blocked", PendingOnTheApi)]
    public async Task Reactivating_someone_who_never_verified_goes_back_to_pending_and_never_to_active(string stoppedAs, string reactivatedAs)
    {
        var (client, _) = await DirectorAsync();
        var (pending, oldLink) = await PendingUserAsync(client.Director);
        await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, stoppedAs)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using var reactivated = await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, reactivatedAs));

        reactivated.StatusCode.Should().Be(HttpStatusCode.OK, await _factory.ServerLog.DescribeAsync(reactivated));
        (await reactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be(PendingOnTheApi, "the answer tells the truth");
        (await RowAsync(pending)).Status.Should().Be(UserStatus.PendingVerification);
        (await LoginStatusAsync(pending)).Should().NotBe(HttpStatusCode.OK);
        (await Verify(oldLink)).StatusCode.Should().NotBe(HttpStatusCode.OK, "stopping the person ended that link");

        // and the regular way still works
        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/resend-verification", new { email = pending.Email })
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await Verify((await RowAsync(pending)).VerificationToken!).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await RowAsync(pending)).Status.Should().Be(UserStatus.Active);
    }

    /// <summary>Only who never proved the address goes back to pending: everyone else keeps being reactivated as before.</summary>
    [Fact]
    public async Task A_verified_user_who_was_blocked_is_reactivated_as_active()
    {
        var (client, _) = await DirectorAsync();
        var (pending, link) = await PendingUserAsync(client.Director);
        await Verify(link).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, "blocked")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, "active")).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await RowAsync(pending)).Status.Should().Be(UserStatus.Active);
        (await LoginStatusAsync(pending)).Should().Be(HttpStatusCode.OK);
    }

    // ── The address ───────────────────────────────────────────────────────────

    /// <summary>The link proved nothing about the new address: following it must not activate the account under an address nobody checked.</summary>
    [Fact]
    public async Task Changing_the_address_of_a_pending_user_ends_the_link_sent_to_the_old_one()
    {
        var (client, _) = await DirectorAsync();
        var (pending, oldLink) = await PendingUserAsync(client.Director);
        var newEmail = $"novo-{Guid.NewGuid():N}"[..16] + $"@{TestCredentials.TestDomain}";

        await client.Http.PutAsJsonAsync(RouteOf(pending), Body(pending, PendingOnTheApi, email: newEmail)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await Verify(oldLink)).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await RowAsync(pending)).Status.Should().Be(UserStatus.PendingVerification);

        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/resend-verification", new { email = newEmail })
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        var after = await RowAsync(pending);
        after.Email.Should().Be(newEmail);
        await Verify(after.VerificationToken!).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await RowAsync(pending)).Status.Should().Be(UserStatus.Active);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record DirectorClient(HttpClient Http, SyntheticAccount Director);

    private static string RouteOf(SyntheticAccount user) => $"{UsersRoute}/{user.UserId}";

    private static object Body(SyntheticAccount user, string? status, string role = "vendedor", string? fullName = null, string? email = null)
        => new { fullName = fullName ?? user.FullName, email = email ?? user.Email, role, phone = (string?)null, notes = (string?)null, status };

    private static string HashOf(string token)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private async Task<(DirectorClient Client, SyntheticAccount Director)> DirectorAsync()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var http = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        return (new DirectorClient(http, director), director);
    }

    /// <summary>A user waiting for the e-mail link, with the link they were sent.</summary>
    private async Task<(SyntheticAccount User, string Link)> PendingUserAsync(SyntheticAccount colleague, UserRole role = UserRole.Vendedor)
    {
        var user = await SyntheticTenant.AddUserAsync(_factory, colleague, role, UserStatus.PendingVerification);
        var link = Guid.NewGuid().ToString("N");
        await ChangeAsync(user, u => u.SetVerificationToken(link, DateTime.UtcNow.AddHours(24)));
        return (user, link);
    }

    private Task<HttpResponseMessage> Verify(string link)
        => _factory.CreateApiClient().GetAsync($"/api/auth/verify-email?token={link}");

    private async Task<HttpStatusCode> LoginStatusAsync(SyntheticAccount user)
    {
        using var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(user.Login, user.Password));
        return response.StatusCode;
    }

    private async Task ShouldBeRefusedAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await _factory.ServerLog.DescribeAsync(response));
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(code);
    }

    private async Task<User> RowAsync(SyntheticAccount user)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == user.UserId);
    }

    private async Task ChangeAsync(SyntheticAccount user, Action<User> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        change(await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.UserId));
        await db.SaveChangesAsync();
    }
}
