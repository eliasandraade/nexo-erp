using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Infrastructure.Email;
using Xunit;

namespace Nexo.UnitTests.Integrations;

/// <summary>
/// The name in this e-mail is typed by whoever fills in the sign-up form, anonymously and against any address, and the message goes
/// out from the company's own domain: it must arrive as text, never as markup.
/// </summary>
public class ResendEmailServiceTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static async Task<string> HtmlSentAsync(string name, string url)
    {
        var capture = new Capture();
        var config = new ConfigurationBuilder().Build();
        var service = new ResendEmailService(new HttpClient(capture), config, NullLogger<ResendEmailService>.Instance);

        await service.SendVerificationEmailAsync("pessoa@teste.dev", name, url);

        return JsonDocument.Parse(capture.Body!).RootElement.GetProperty("html").GetString()!;
    }

    [Theory]
    [InlineData("<a href=\"https://evil.test\">Clique aqui</a><!--")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Ana\"><script>alert(1)</script>")]
    public async Task A_name_arrives_as_text_and_never_as_markup(string name)
    {
        var html = await HtmlSentAsync(name, "https://erp.orken.com.br/verify-email?token=abc");

        html.Should().NotContain(name);
        html.Should().NotContain("evil.test\">");
        html.Should().NotContain("<img src=x");
        html.Should().NotContain("<script>");
        html.Should().Contain(WebUtility.HtmlEncode(name));
    }

    [Fact]
    public async Task The_button_still_carries_the_address_with_its_token()
    {
        var html = await HtmlSentAsync("Ana", "https://erp.orken.com.br/verify-email?token=abc&x=1");

        html.Should().Contain("Olá, Ana.");
        html.Should().Contain("href=\"https://erp.orken.com.br/verify-email?token=abc&amp;x=1\"");
    }

    [Fact]
    public async Task An_address_cannot_break_out_of_the_link()
    {
        var html = await HtmlSentAsync("Ana", "https://erp.orken.com.br/v\"><script>alert(1)</script>");

        html.Should().NotContain("<script>");
    }
}
