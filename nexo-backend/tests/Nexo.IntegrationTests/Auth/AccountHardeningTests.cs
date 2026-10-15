using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// PR 64 — per-account lockout (and its audit trail), the manager's temporary password forcing a
/// change, and inviting a user by e-mail. Each test uses its own user — never the shared admin.
/// </summary>
[Collection("Integration")]
public class AccountHardeningTests
{
    private readonly TestWebApplicationFactory _factory;
    public AccountHardeningTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string Fresh() => "t" + Guid.NewGuid().ToString("N")[..12];

    private async Task<(Guid Id, string Login, string Password)> NewUserAsync(HttpClient admin)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var password = Fresh();
        var r = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Pessoa Teste", email = $"hd{tag}@teste.dev", login = $"hd{tag}", password, role = "vendedor",
            phone = (string?)null, notes = (string?)null,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return ((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), $"hd{tag}", password);
    }

    private Task<HttpResponseMessage> LoginAsync(string login, string password)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(login, password));

    [Fact]
    public async Task Ten_wrong_passwords_pause_the_account_even_for_the_right_one()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var user = await NewUserAsync(admin);

        for (var i = 0; i < 9; i++)
            (await LoginAsync(user.Login, Fresh())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var tenth = await LoginAsync(user.Login, Fresh());
        tenth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await tenth.Content.ReadAsStringAsync()).Should().Contain("account_locked");

        (await LoginAsync(user.Login, user.Password)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "while paused the password isn't even checked");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var actions = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EntityId == user.Id.ToString()).Select(a => a.ActionType).ToListAsync();
        actions.Should().Contain("user_login_failed").And.Contain("user_login_locked");
    }

    [Fact]
    public async Task A_right_password_clears_the_wrong_ones_and_is_audited()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var user = await NewUserAsync(admin);
        for (var i = 0; i < 9; i++) await LoginAsync(user.Login, Fresh());
        (await LoginAsync(user.Login, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        for (var i = 0; i < 9; i++) await LoginAsync(user.Login, Fresh());
        (await LoginAsync(user.Login, user.Password)).StatusCode.Should().Be(HttpStatusCode.OK, "the count started over");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.AuditRecords.IgnoreQueryFilters().CountAsync(a => a.EntityId == user.Id.ToString() && a.ActionType == "user_logged_in"))
            .Should().Be(2);
    }

    [Fact]
    public async Task A_password_set_by_the_manager_must_be_replaced_at_the_next_sign_in()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var user = await NewUserAsync(admin);
        var temporary = Fresh();
        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/admin-reset-password", new { newPassword = temporary }))
            .IsSuccessStatusCode.Should().BeTrue();

        var login = await (await LoginAsync(user.Login, temporary)).Content.ReadFromJsonAsync<JsonElement>();
        login.GetProperty("session").GetProperty("requirePasswordChange").GetBoolean().Should().BeTrue();

        var (client, _) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, temporary);
        var chosen = Fresh();
        (await client.PostAsJsonAsync($"/api/users/{user.Id}/change-password", new { currentPassword = temporary, newPassword = chosen }))
            .IsSuccessStatusCode.Should().BeTrue();
        var after = await (await LoginAsync(user.Login, chosen)).Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("session").GetProperty("requirePasswordChange").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_invited_user_chooses_the_password_through_the_link()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var r = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Convidada", email = $"inv{tag}@teste.dev", login = $"inv{tag}", password = "", role = "vendedor",
            phone = (string?)null, notes = (string?)null, sendInvite = true,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var u = await db.Users.IgnoreQueryFilters().FirstAsync(x => x.Id == id);
            u.PasswordResetExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(72), TimeSpan.FromMinutes(2));
            u.SetPasswordResetToken(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("convite-1"))).ToLowerInvariant(),
                DateTime.UtcNow.AddHours(72));
            await db.SaveChangesAsync();
        }

        var chosen = Fresh();
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/reset-password", new { token = "convite-1", newPassword = chosen }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync($"inv{tag}", chosen)).StatusCode.Should().Be(HttpStatusCode.OK);

        // without an e-mail there is nobody to invite
        (await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Sem Email", email = "", login = $"nm{tag}", password = "", role = "vendedor",
            phone = (string?)null, notes = (string?)null, sendInvite = true,
        })).IsSuccessStatusCode.Should().BeFalse();
    }

    private async Task<string?> ResetHashAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<NexoDbContext>().Users.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(u => u.Id == userId)).PasswordResetTokenHash;
    }

    [Fact]
    public async Task Fixing_the_email_or_setting_a_password_kills_a_pending_invite()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var created = await (await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Digitado Errado", email = $"errado{tag}@teste.dev", login = $"err{tag}", password = "", role = "vendedor",
            phone = (string?)null, notes = (string?)null, sendInvite = true,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        (await ResetHashAsync(id)).Should().NotBeNull();

        (await admin.PutAsJsonAsync($"/api/users/{id}", new
        {
            fullName = "Digitado Errado", email = $"certo{tag}@teste.dev", phone = (string?)null, role = "vendedor", notes = (string?)null,
        })).IsSuccessStatusCode.Should().BeTrue();
        (await ResetHashAsync(id)).Should().BeNull("the link sent to the wrong address must stop working");

        (await admin.PostAsync($"/api/users/{id}/resend-invite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ResetHashAsync(id)).Should().NotBeNull();
        (await admin.PostAsJsonAsync($"/api/users/{id}/admin-reset-password", new { newPassword = Fresh() })).IsSuccessStatusCode.Should().BeTrue();
        (await ResetHashAsync(id)).Should().BeNull("a password set by the manager overrides the invite");
    }

    [Fact]
    public async Task Resetting_the_password_unlocks_the_account_and_an_invite_doesnt_block_forgot_password()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var user = await NewUserAsync(admin);
        for (var i = 0; i < 10; i++) await LoginAsync(user.Login, Fresh());
        (await LoginAsync(user.Login, user.Password)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var renewed = Fresh();
        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/admin-reset-password", new { newPassword = renewed })).IsSuccessStatusCode.Should().BeTrue();
        (await LoginAsync(user.Login, renewed)).StatusCode.Should().Be(HttpStatusCode.OK, "a new password ends the lockout");

        // a pending 72 h invite must not hold back a forgot-password request
        (await admin.PostAsync($"/api/users/{user.Id}/resend-invite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var inviteHash = await ResetHashAsync(user.Id);
        var email = (await admin.GetFromJsonAsync<JsonElement>($"/api/users/{user.Id}")).GetProperty("email").GetString();
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await ResetHashAsync(user.Id)).Should().NotBe(inviteHash);
    }

    [Fact]
    public async Task Parallel_wrong_guesses_still_lock_the_account()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var user = await NewUserAsync(admin);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => LoginAsync(user.Login, Fresh())));
        (await LoginAsync(user.Login, user.Password)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the counter is atomic: concurrent guesses can't share one slot");
    }
}
