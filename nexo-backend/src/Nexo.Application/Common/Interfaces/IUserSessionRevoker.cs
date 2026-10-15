namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Ends every session of a user: refresh tokens removed, session rows marked revoked, and the
/// cached security stamp dropped so access tokens die on the next request. The caller bumps the
/// user's SecurityStamp (that is what kills tokens not tracked as sessions) and saves.
/// </summary>
public interface IUserSessionRevoker
{
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Call after the save: drops a stamp a racing request may have re-cached meanwhile.</summary>
    Task ForgetStampAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Invites a just-created user by e-mail: a link (72 h) to choose their own password.</summary>
public interface IUserInviter
{
    Task SendInviteAsync(Guid userId, CancellationToken ct = default);
}
