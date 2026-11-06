using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// The signing-secret guard on the real startup (Program.cs): JwtSecretPolicyTests prove the rule, these prove the host
/// applies it. Without Jwt__Secret the host reads the placeholder that appsettings.json ships, so that value stands for
/// "the variable is missing".
/// </summary>
[Collection("Integration")]
public class JwtStartupGuardTests
{
    private readonly TestWebApplicationFactory _factory;

    public JwtStartupGuardTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string ShippedPlaceholder => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json")
        .Build()["Jwt:Secret"]!;

    public static TheoryData<string, string, string> UnsafeSecrets()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var environment in new[] { "Production", "Staging" })
        {
            data.Add(environment, ShippedPlaceholder, "committed to the repository");
            data.Add(environment, "", "not configured");
            data.Add(environment, "   ", "not configured");
            data.Add(environment, "short-but-random-Q7x!", "at least 32 characters");
            data.Add(environment, new string('a', 20) + new string('b', 20), "too little variety");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(UnsafeSecrets))]
    public void The_host_refuses_to_start_with_an_unsafe_secret(string environment, string secret, string reason)
    {
        using var host = HostWith(environment, secret);

        var start = () => host.CreateClient();

        var message = start.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("Refusing to start").And.Contain(reason);
        if (!string.IsNullOrWhiteSpace(secret))
            message.Should().NotContain(secret, "this message goes to the logs");
    }

    private WebApplicationFactory<Program> HostWith(string environment, string secret)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Jwt:Secret", secret);
        });
}
