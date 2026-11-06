namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Ends every session of a user: refresh tokens removed and session rows marked revoked. The caller
/// bumps the user's SecurityStamp and saves: once committed, every access token already out there
/// dies on its next request (the stamp is checked against the database) and no refresh goes through.
/// That commit also closes the person's open hub connections (HubSessionChangeInterceptor): nothing to call for it.
/// </summary>
public interface IUserSessionRevoker
{
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Invites a just-created user by e-mail: a link (72 h) to choose their own password.</summary>
public interface IUserInviter
{
    /// <summary>False when no e-mail provider is configured — an invite would never arrive.</summary>
    bool CanInvite { get; }
    Task SendInviteAsync(Guid userId, CancellationToken ct = default);
}
