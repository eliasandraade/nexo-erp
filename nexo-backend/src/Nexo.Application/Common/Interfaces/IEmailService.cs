namespace Nexo.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(
        string toEmail,
        string toName,
        string verificationUrl,
        CancellationToken ct = default);

    /// <summary>Sends one transactional e-mail. False when the provider refused it (already logged).</summary>
    Task<bool> SendAsync(string toEmail, string subject, string html, CancellationToken ct = default);
}
