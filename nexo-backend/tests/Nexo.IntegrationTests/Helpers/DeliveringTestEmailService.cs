using Nexo.Application.Common.Interfaces;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>An e-mail provider that accepts everything and sends nothing (test host only).</summary>
public sealed class DeliveringTestEmailService : IEmailService
{
    public bool CanDeliver => true;

    public Task SendVerificationEmailAsync(string toEmail, string toName, string verificationUrl, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<EmailSendResult> SendAsync(string toEmail, string subject, string html, CancellationToken ct = default)
        => Task.FromResult(EmailSendResult.Sent);
}
