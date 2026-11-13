using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>What is typed in the sign-up form becomes a JSON document of the new company: it cannot break that document or add keys to it.</summary>
[Collection("Integration")]
public class RegistrationSettingsJsonTests
{
    private readonly TestWebApplicationFactory _factory;

    public RegistrationSettingsJsonTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("Ana termina em barra \\")]
    [InlineData("Ana \"entre aspas\"")]
    [InlineData("Ana\",\"tradeName\":\"injetado")]
    [InlineData("Ana com linha\nquebrada")]
    public async Task A_name_becomes_a_value_of_the_company_settings_and_nothing_else(string name)
    {
        var email = $"c{Guid.NewGuid():N}"[..20] + "@teste.dev";

        using var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register", new
        {
            name,
            email,
            password = TestCredentials.SameTenantManagerPassword,
        });
        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await _factory.ServerLog.DescribeAsync(response));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenantId = await db.Users.IgnoreQueryFilters().Where(u => u.Email == email).Select(u => u.TenantId).SingleAsync();
        var settings = await db.AppSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId);

        using var company = JsonDocument.Parse(settings.CompanySettingsJson);
        company.RootElement.GetProperty("name").GetString().Should().Be(name.Trim());
        company.RootElement.GetProperty("tradeName").GetString().Should().Be(name.Trim());
        company.RootElement.GetProperty("email").GetString().Should().Be(email);
        company.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["name", "tradeName", "cnpj", "email", "phone"]);
    }

    /// <summary>The login is the e-mail, in a column of 50: a longer address used to answer 500 with the company already saved.</summary>
    [Fact]
    public async Task An_address_too_long_for_the_login_is_refused_and_leaves_nothing_behind()
        => await ExpectRefusedAsync($"Empresa {Guid.NewGuid():N}", new string('a', 45) + "@teste.dev");

    [Fact]
    public async Task A_name_too_long_for_its_column_is_refused_and_leaves_nothing_behind()
        => await ExpectRefusedAsync($"Empresa {Guid.NewGuid():N}" + new string('n', 150), $"c{Guid.NewGuid():N}"[..20] + "@teste.dev");

    private async Task ExpectRefusedAsync(string name, string email)
    {

        using var response = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register", new
        {
            name,
            email,
            password = TestCredentials.SameTenantManagerPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await _factory.ServerLog.DescribeAsync(response));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Tenants.CountAsync(t => t.CompanyName == name)).Should().Be(0);
    }
}
