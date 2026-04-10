using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Audit;

/// <summary>
/// Stages AuditRecord entities into the EF Core change tracker.
/// The record is saved as part of the caller's transaction — this service
/// NEVER calls SaveChangesAsync directly.
///
/// IP address is captured automatically from the current HTTP request context
/// so callers don't need to pass it explicitly. Works for both IPv4 and IPv6.
/// Behind a reverse proxy, set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so that
/// X-Forwarded-For is translated into the real RemoteIpAddress.
/// </summary>
public class AuditWriterService : IAuditWriter
{
    private readonly NexoDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditWriterService(NexoDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context             = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public void Stage(
        string actionType,
        string severity,
        string entityType,
        string entityId,
        string description,
        Guid? tenantId = null,
        Guid? actorId = null,
        string? actorName = null,
        string actorType = "user",
        object? metadata = null)
    {
        var metadataJson = metadata is not null
            ? JsonSerializer.Serialize(metadata, new JsonSerializerOptions
              {
                  PropertyNamingPolicy = JsonNamingPolicy.CamelCase
              })
            : null;

        var ipAddress = _httpContextAccessor.HttpContext?
            .Connection.RemoteIpAddress?
            .ToString();

        var record = AuditRecord.Create(
            actionType:   actionType,
            severity:     severity,
            entityType:   entityType,
            entityId:     entityId,
            description:  description,
            tenantId:     tenantId,
            actorId:      actorId,
            actorName:    actorName,
            actorType:    actorType,
            metadataJson: metadataJson,
            ipAddress:    ipAddress);

        _context.AuditRecords.Add(record);
    }
}
