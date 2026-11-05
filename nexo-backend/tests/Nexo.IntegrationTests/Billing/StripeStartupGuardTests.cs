using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Billing;

/// <summary>
/// A guarda na subida real da aplicação (Program.cs, com banco de verdade): o host recusa subir com a configuração errada,
/// e produção com chave live sobe e atende. Nada aqui chama o Stripe: subir não fala com ele, e o único pedido ao webhook
/// é recusado antes por falta de assinatura.
/// </summary>
[Collection("Integration")]
public class StripeStartupGuardTests
{
    // Montados em tempo de execução e obviamente falsos: nenhum literal parecido com uma chave real.
    private static readonly string LiveKey = "sk_" + "live_" + "chave-falsa-dos-testes";
    private const string TestKey = "sk_test_chave-falsa-dos-testes";
    private const string Secret = "whsec_segredo-falso-dos-testes";

    // Fora de Development e Testing a própria subida exige um segredo JWT aleatório (JwtSecretPolicy).
    private static readonly string RandomJwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private readonly TestWebApplicationFactory _factory;

    public StripeStartupGuardTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void A_live_key_outside_production_stops_the_host(string environment)
        => ExpectRefusal(Host(environment, stripeEnabled: "true", secretKey: LiveKey, webhookSecret: Secret), "live Stripe key");

    [Fact]
    public void A_live_key_outside_production_stops_the_host_even_with_stripe_off()
        => ExpectRefusal(Host("Staging", stripeEnabled: "false", secretKey: LiveKey, webhookSecret: ""), "live Stripe key");

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Stripe_on_without_the_webhook_secret_stops_the_host(string environment)
        => ExpectRefusal(Host(environment, stripeEnabled: "true", secretKey: TestKey, webhookSecret: ""), "WebhookSecret");

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Stripe_on_without_the_secret_key_stops_the_host(string environment)
        => ExpectRefusal(Host(environment, stripeEnabled: "true", secretKey: "", webhookSecret: Secret), "SecretKey");

    [Fact]
    public async Task Production_with_a_live_key_starts_and_serves()
    {
        await using var host = Host("Production", stripeEnabled: "true", secretKey: LiveKey, webhookSecret: Secret);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await client.GetAsync(LiveProbe)).StatusCode.Should().Be(HttpStatusCode.OK);

        // o webhook está ligado: responde pela assinatura ausente, e não 404 de flag desligada
        var webhook = await client.PostAsync("/api/billing/webhook", new StringContent("{}"));
        webhook.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staging_with_a_test_mode_key_starts_and_serves()
    {
        await using var host = Host("Staging", stripeEnabled: "true", secretKey: TestKey, webhookSecret: Secret);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await client.GetAsync(LiveProbe)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Production_with_stripe_off_and_no_keys_starts_as_before()
    {
        await using var host = Host("Production", stripeEnabled: "false", secretKey: "", webhookSecret: "");
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await client.GetAsync(LiveProbe)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/api/billing/webhook", new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private const string LiveProbe = "/health/live";

    private static void ExpectRefusal(WebApplicationFactory<Program> host, string reason)
    {
        var start = () => host.CreateClient();

        var message = start.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("Refusing to start").And.Contain(reason);
        message.Should().NotContain("falsa", "a mensagem vai para os logs");
    }

    private WebApplicationFactory<Program> Host(string environment, string stripeEnabled, string secretKey, string webhookSecret)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Jwt:Secret", RandomJwtSecret);
            b.UseSetting("Integrations:Features:StripeEnabled", stripeEnabled);
            b.UseSetting("Integrations:Stripe:SecretKey", secretKey);
            b.UseSetting("Integrations:Stripe:WebhookSecret", webhookSecret);
            // Fora de Testing os jobs periódicos sobem junto: não podem mexer nos dados dos outros testes da coleção.
            b.ConfigureTestServices(s =>
            {
                foreach (var job in s.Where(d => d.ServiceType == typeof(IHostedService)
                                                 && d.ImplementationType?.Namespace?.StartsWith("Nexo.", StringComparison.Ordinal) == true).ToList())
                    s.Remove(job);
            });
        });
}
