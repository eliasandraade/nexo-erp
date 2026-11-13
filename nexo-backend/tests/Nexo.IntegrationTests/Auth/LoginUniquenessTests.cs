using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Auth;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// One login, one account, in the whole system. Sign-in receives a login and no company, so every way of creating an account
/// (the directors' screen, the public sign-up, the platform) refuses a login that exists anywhere, in any spelling, and the
/// database refuses it too when two requests arrive together. On the real API and a real PostgreSQL.
/// </summary>
[Collection("Integration")]
public class LoginUniquenessTests
{
    private readonly TestWebApplicationFactory _factory;
    public LoginUniquenessTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string Secret() => "Ax!" + Guid.NewGuid().ToString("N")[..20];   // a new one each run: no credential in the code
    private static string NewLogin() => "uni." + Guid.NewGuid().ToString("N")[..12];
    private static string NewEmail() => $"uni{Guid.NewGuid():N}"[..20] + "@teste.dev";

    private static object NewUser(string login) => new
    {
        fullName = "Pessoa de Teste",
        email = NewEmail(),
        login,
        password = Secret(),
        role = "vendedor",
        phone = (string?)null,
        notes = (string?)null,
    };

    private async Task<HttpClient> DirectorOfANewCompanyAsync()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, "varejo");
        return await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
    }

    private async Task<int> AccountsWithLoginAsync(string login)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.Users.IgnoreQueryFilters().CountAsync(u => u.Login == login);
    }

    private async Task<int> CompaniesWithEmailAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Email == email);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    // ── The directors' screen ─────────────────────────────────────────────────

    [Fact]
    public async Task A_login_taken_in_one_company_is_refused_in_another()
    {
        var first = await DirectorOfANewCompanyAsync();
        var second = await DirectorOfANewCompanyAsync();
        var login = NewLogin();
        (await first.PostAsJsonAsync("/api/users", NewUser(login))).IsSuccessStatusCode.Should().BeTrue();

        var again = await second.PostAsJsonAsync("/api/users", NewUser(login));

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(again)).Should().Be("login_taken");
        (await again.Content.ReadAsStringAsync()).Should().NotContain("Sintetica", "the answer does not say where the login is taken");
        (await AccountsWithLoginAsync(login)).Should().Be(1);
    }

    [Fact]
    public async Task Another_spelling_of_a_taken_login_is_the_same_login()
    {
        var first = await DirectorOfANewCompanyAsync();
        var second = await DirectorOfANewCompanyAsync();
        var login = NewLogin();
        (await first.PostAsJsonAsync("/api/users", NewUser(login))).IsSuccessStatusCode.Should().BeTrue();

        var otherCompany = await second.PostAsJsonAsync("/api/users", NewUser(login.ToUpperInvariant()));
        var sameCompany = await first.PostAsJsonAsync("/api/users", NewUser(login.ToUpperInvariant()));

        otherCompany.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(otherCompany)).Should().Be("login_taken");
        sameCompany.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AccountsWithLoginAsync(login)).Should().Be(1);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    public async Task The_login_of_the_first_administrator_cannot_be_created_again_and_keeps_working(string spelling)
    {
        var director = await DirectorOfANewCompanyAsync();

        var response = await director.PostAsJsonAsync("/api/users", NewUser(spelling));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("login_taken");
        (await AccountsWithLoginAsync(TestCredentials.AdminLogin)).Should().Be(1);
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload()))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the existing account is untouched");
    }

    [Fact]
    public async Task Many_companies_asking_for_one_login_at_once_create_it_once()
    {
        var directors = new[] { await DirectorOfANewCompanyAsync(), await DirectorOfANewCompanyAsync(), await DirectorOfANewCompanyAsync() };
        var login = NewLogin();

        var answers = await Task.WhenAll(Enumerable.Range(0, 9).Select(i => directors[i % directors.Length].PostAsJsonAsync("/api/users", NewUser(login))));

        answers.Count(a => a.IsSuccessStatusCode).Should().Be(1, "one login is one account");
        foreach (var refused in answers.Where(a => !a.IsSuccessStatusCode))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "losing the race is a conflict, never a 500");
            (await CodeOf(refused)).Should().Be("login_taken", "whoever lost hears the same as whoever arrived later");
        }
        (await AccountsWithLoginAsync(login)).Should().Be(1);
    }

    // ── The database ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_database_refuses_a_repeated_login_whatever_the_code_checked()
    {
        var first = await SyntheticTenant.CreateAsync(_factory);
        var second = await SyntheticTenant.CreateAsync(_factory);
        var login = NewLogin();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Secret());
        db.Users.Add(User.Create(first.TenantId, "Pessoa Um", NewEmail(), login, hash, UserRole.Vendedor));
        await db.SaveChangesAsync();

        // The same login written straight into another company, with no check in between.
        db.Users.Add(User.Create(second.TenantId, "Pessoa Dois", NewEmail(), $"  {login.ToUpperInvariant()} ", hash, UserRole.Vendedor));
        var save = async () => await db.SaveChangesAsync();

        var refused = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        LoginRegistry.LostTheLogin(refused).Should().BeTrue("the unique index on the login is what refused it");
        (await AccountsWithLoginAsync(login)).Should().Be(1);
    }

    // ── The public sign-up ────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_up_with_an_address_that_is_already_a_login_is_refused_and_creates_no_company()
    {
        // A login shaped like an e-mail, as the platform may give to a company's first user.
        var company = await SyntheticTenant.CreateAsync(_factory);
        var address = NewEmail();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.Users.Add(User.Create(company.TenantId, "Pessoa", NewEmail(), address,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Secret()), UserRole.Vendedor));
            await db.SaveChangesAsync();
        }

        var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Nome Completo", email = address.ToUpperInvariant(), password = Secret() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("email_already_registered");
        (await CompaniesWithEmailAsync(address)).Should().Be(0);
        (await AccountsWithLoginAsync(address)).Should().Be(1);
    }

    [Fact]
    public async Task Signing_up_with_the_address_of_a_platform_operator_is_refused_and_creates_no_company()
    {
        // The shared sign-in form tries the platform first: a company user with this login could never enter.
        var address = NewEmail();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(address, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Secret())));
            await db.SaveChangesAsync();
        }

        var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Nome Completo", email = address, password = Secret() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CompaniesWithEmailAsync(address)).Should().Be(0);
        (await AccountsWithLoginAsync(address)).Should().Be(0);
    }

    [Fact]
    public async Task Sign_ups_for_one_address_at_once_create_one_account_and_leave_nothing_else_behind()
    {
        var address = NewEmail();
        var client = _factory.CreateApiClient();

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.PostAsJsonAsync("/api/auth/register", new { name = "Nome Completo", email = address, password = Secret() })));

        answers.Count(a => a.StatusCode == HttpStatusCode.OK).Should().Be(1, "one address is one account");
        foreach (var refused in answers.Where(a => a.StatusCode != HttpStatusCode.OK))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "losing the race is a conflict, never a 500");
            (await CodeOf(refused)).Should().Be("email_already_registered");
        }
        (await AccountsWithLoginAsync(address)).Should().Be(1);
        (await CompaniesWithEmailAsync(address)).Should().Be(1, "whoever lost the race leaves no company without a user");
    }

    // ── The platform ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_platform_cannot_give_a_new_company_a_login_that_exists_elsewhere()
    {
        var existing = await SyntheticTenant.CreateAsync(_factory);
        var platform = await AuthClientFactory.LoginAsPlatformAsync(_factory);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyEmail = $"uni-{suffix}@teste.delete";

        var response = await platform.PostAsJsonAsync("/api/platform/tenants", new
        {
            companyName = $"TESTE_DELETE Login Unico {suffix}",
            taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString(),
            email = companyEmail,
            tradeName = (string?)null,
            phone = (string?)null,
            businessType = "varejo",
            modules = new[] { "varejo" },
            adminName = "Pessoa de Teste",
            adminLogin = $" {existing.Login.ToUpperInvariant()} ",
            adminPassword = Secret(),
            adminEmail = NewEmail(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("login_taken");
        (await CompaniesWithEmailAsync(companyEmail)).Should().Be(0);
        (await AccountsWithLoginAsync(existing.Login)).Should().Be(1);
    }

    // ── What keeps working ────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_in_and_the_manager_check_accept_the_login_in_any_spelling()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var typed = $"  {director.Login.ToUpperInvariant()} ";

        var signIn = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(typed, director.Password));
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var check = await client.PostAsJsonAsync("/api/users/validate-manager", new { login = typed, password = director.Password });
        check.StatusCode.Should().Be(HttpStatusCode.OK);
        (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_new_login_is_stored_in_one_spelling_and_signs_in()
    {
        var director = await DirectorOfANewCompanyAsync();
        var login = NewLogin();
        var password = Secret();

        var created = await director.PostAsJsonAsync("/api/users", new
        {
            fullName = "Pessoa de Teste",
            email = NewEmail(),
            login = login.ToUpperInvariant(),
            password,
            role = "vendedor",
            phone = (string?)null,
            notes = (string?)null,
        });

        created.IsSuccessStatusCode.Should().BeTrue();
        (await AccountsWithLoginAsync(login)).Should().Be(1, "the login is stored in lower case, whatever was typed");
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(login, password)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
