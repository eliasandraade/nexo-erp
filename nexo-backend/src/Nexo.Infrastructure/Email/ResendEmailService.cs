using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Email;

/// <summary>
/// Sends transactional emails via the Resend API (https://resend.com).
/// Requires: Resend__ApiKey environment variable.
/// </summary>
public class ResendEmailService : IEmailService
{
    private const string ResendApiUrl = "https://api.resend.com/emails";
    private const string FromAddress  = "NexoERP <noreply@nexoerp.com.br>";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        HttpClient http,
        IConfiguration configuration,
        ILogger<ResendEmailService> logger)
    {
        _http   = http;
        _apiKey = configuration["Resend:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(
        string toEmail,
        string toName,
        string verificationUrl,
        CancellationToken ct = default)
    {
        var subject = "Confirme seu e-mail — NexoERP";
        var html    = BuildVerificationHtml(toName, verificationUrl);

        var payload = new
        {
            from    = FromAddress,
            to      = new[] { toEmail },
            subject = subject,
            html    = html,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, ResendApiUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Resend API error {Status} sending to {Email}: {Error}",
                (int)response.StatusCode, toEmail, error);
            // Don't throw — the account is created; the user can resend from /check-email.
        }
        else
        {
            _logger.LogInformation(
                "Verification email sent to {Email} via Resend", toEmail);
        }
    }

    private static string BuildVerificationHtml(string name, string verificationUrl)
    {
        return $"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head><meta charset="UTF-8" /><meta name="viewport" content="width=device-width,initial-scale=1" /></head>
        <body style="margin:0;padding:0;background:#f4f6fa;font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6fa;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06);">

                <!-- Header -->
                <tr>
                  <td style="background:#13214a;padding:28px 40px;">
                    <span style="font-size:20px;font-weight:700;color:#ffffff;letter-spacing:-0.5px;">
                      Nexo<span style="color:#4d90fe;">ERP</span>
                    </span>
                  </td>
                </tr>

                <!-- Body -->
                <tr>
                  <td style="padding:40px 40px 32px;">
                    <p style="margin:0 0 8px;font-size:22px;font-weight:600;color:#13214a;">
                      Olá, {name}.
                    </p>
                    <p style="margin:0 0 28px;font-size:15px;color:#5a6478;line-height:1.6;">
                      Sua conta no NexoERP foi criada. Clique no botão abaixo para confirmar
                      seu e-mail e acessar o sistema.
                    </p>

                    <table cellpadding="0" cellspacing="0">
                      <tr>
                        <td style="border-radius:8px;background:#4d90fe;">
                          <a href="{verificationUrl}"
                             style="display:inline-block;padding:14px 32px;font-size:15px;font-weight:600;
                                    color:#ffffff;text-decoration:none;letter-spacing:0.2px;">
                            Confirmar e-mail
                          </a>
                        </td>
                      </tr>
                    </table>

                    <p style="margin:28px 0 0;font-size:13px;color:#9aa3b2;line-height:1.6;">
                      Este link expira em 24 horas. Se você não criou uma conta, ignore este e-mail.
                    </p>
                  </td>
                </tr>

                <!-- Footer -->
                <tr>
                  <td style="padding:20px 40px;border-top:1px solid #edf0f5;">
                    <p style="margin:0;font-size:12px;color:#b0b8c9;">
                      NexoERP — Gestão inteligente para empresas reais.<br />
                      Andrade Systems · Ceará, Brasil
                    </p>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }
}
