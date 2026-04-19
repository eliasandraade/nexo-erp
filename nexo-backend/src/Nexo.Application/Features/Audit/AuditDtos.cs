namespace Nexo.Application.Features.Audit;

public record AuditRecordDto(
    string Id,
    string Timestamp,
    string ActionType,
    string Severity,
    string? ActorName,
    string ActorType,
    string EntityType,
    string EntityId,
    string Description,
    string? MetadataJson,
    string? IpAddress);

public record AuditStatsDto(int Total, int Critical, int Warning, int Info);
