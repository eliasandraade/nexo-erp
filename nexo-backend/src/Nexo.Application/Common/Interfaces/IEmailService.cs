namespace Nexo.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(
        string toEmail,
        string toName,
        string verificationUrl,
        CancellationToken ct = default);
}
