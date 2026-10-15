namespace Nexo.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(
        string toEmail,
        string toName,
        string verificationUrl,
        CancellationToken ct = default);

    /// <summary>False when no real provider is configured (e-mails only go to the log).</summary>
    bool CanDeliver { get; }

    /// <summary>Sends one transactional e-mail and says what happened (failures are already logged).</summary>
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string html, CancellationToken ct = default);
}

public enum EmailSendResult
{
    Sent,
    /// <summary>Refused for good (bad address, …) — trying again would not help.</summary>
    Rejected,
    /// <summary>Temporary (rate limit, provider down, timeout) — worth trying again later.</summary>
    Failed,
}
