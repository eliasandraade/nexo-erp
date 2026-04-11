using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Email;

/// <summary>
/// Development/MVP email service — logs to console instead of sending real emails.
/// Visible in Railway logs. Replace with Resend/SMTP for production.
/// </summary>
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;
    public ConsoleEmailService(ILogger<ConsoleEmailService> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string toEmail, string toName, string verificationUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "=== VERIFICATION EMAIL ===\nTo: {Email}\nName: {Name}\nLink: {Url}\n=========================",
            toEmail, toName, verificationUrl);
        return Task.CompletedTask;
    }
}
