using Microsoft.AspNetCore.Http;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Writes Build events (portal, schedule, cost per stage) to the audit trail with who, tenant, entity and
/// request id. Staged in the caller's unit of work — saved with the change it describes, or not at all.
/// Never receives passwords, hashes or tokens.
/// </summary>
public sealed class BuildAuditTrail
{
    public const string EntityPrefix = "Build";

    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _user;
    private readonly IHttpContextAccessor _http;

    public BuildAuditTrail(IAuditWriter audit, ICurrentUser user, IHttpContextAccessor http)
    {
        _audit = audit;
        _user = user;
        _http = http;
    }

    public Guid UserId => _user.UserId;
    public string UserName => string.IsNullOrWhiteSpace(_user.Name) ? "usuário" : _user.Name;

    public void Record(string action, string entityType, Guid entityId, string description, object? details = null,
        string severity = AuditSeverity.Info)
        => _audit.Stage(
            actionType: action,
            severity: severity,
            entityType: EntityPrefix + entityType,
            entityId: entityId.ToString(),
            description: description,
            tenantId: _user.TenantId,
            actorId: _user.UserId,
            actorName: UserName,
            metadata: new { requestId = _http.HttpContext?.TraceIdentifier, details });

    /// <summary>
    /// A decision taken by the client through a password portal session (change order, selection): the actor is
    /// the name the client typed, the tenant is the portal's.
    /// </summary>
    public void RecordPortalClient(string action, Guid tenantId, string entityType, Guid entityId, string description,
        string? signerName, object? details = null)
        => _audit.Stage(
            actionType: action,
            severity: AuditSeverity.Info,
            entityType: EntityPrefix + entityType,
            entityId: entityId.ToString(),
            description: description,
            tenantId: tenantId,
            actorName: string.IsNullOrWhiteSpace(signerName) ? "Cliente (portal)" : $"Cliente (portal): {signerName.Trim()}",
            actorType: "portal_client",
            metadata: new { requestId = _http.HttpContext?.TraceIdentifier, details });

    /// <summary>An event of a system job (no signed-in user), for an explicit tenant (e.g. the weekly AI digest).</summary>
    public void RecordSystem(string action, Guid tenantId, string entityType, Guid entityId, string description, string actorName,
        object? details = null)
        => _audit.Stage(
            actionType: action,
            severity: AuditSeverity.Info,
            entityType: EntityPrefix + entityType,
            entityId: entityId.ToString(),
            description: description,
            tenantId: tenantId,
            actorName: actorName,
            actorType: "system",
            metadata: new { details });

    /// <summary>An event caused by the anonymous portal visitor (e.g. the portal locking after wrong passwords).</summary>
    public void RecordPortalVisitor(string action, Guid tenantId, Guid portalId, string description)
        => _audit.Stage(
            actionType: action,
            severity: AuditSeverity.Warning,
            entityType: EntityPrefix + "ProjectPortal",
            entityId: portalId.ToString(),
            description: description,
            tenantId: tenantId,
            actorType: "portal_visitor",
            metadata: new { requestId = _http.HttpContext?.TraceIdentifier });
}
