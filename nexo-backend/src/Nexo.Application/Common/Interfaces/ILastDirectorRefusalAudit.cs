namespace Nexo.Application.Common.Interfaces;

/// <summary>One user a refused change would have taken from the active Diretoria: the role and status it asked for, or the deletion.</summary>
public sealed record LastDirectorAttempt(Guid UserId, string Role, string Status, bool Deleted = false);

/// <summary>
/// Records in the security audit that a change was refused for leaving a company without an active Diretoria.
///
/// The refused transaction rolls back, so <see cref="IAuditWriter.Stage"/> on the request's context would go with it, and saving that
/// context would bring back the change that was just refused. This records on a context and a connection of its own.
///
/// It returns at once and never throws: what is decided here is only that the entry goes out. So an audit that fails or hangs does
/// not change the answer of the request, and the company's advisory lock, held by the caller until its transaction ends, never
/// waits for it. The price is that a process that dies right after the refusal may lose the entry; a failure to write it is logged.
///
/// Call it once for each refusal, right before throwing. Identifiers only: no name, e-mail, login or phone goes into the entry.
/// </summary>
public interface ILastDirectorRefusalAudit
{
    /// <param name="tenantId">The company.</param>
    /// <param name="source">Which layer refused: <see cref="LastDirectorRefusalSources"/>.</param>
    /// <param name="attempts">Who the change was about.</param>
    void Record(Guid tenantId, string source, IReadOnlyList<LastDirectorAttempt> attempts);
}

public static class LastDirectorRefusalSources
{
    /// <summary><c>PUT /users/{id}</c>, which checks before changing anything.</summary>
    public const string UserUpdate = "user_update";

    /// <summary>The check in the data context, which no path that saves through EF skips.</summary>
    public const string DataContext = "data_context";
}
