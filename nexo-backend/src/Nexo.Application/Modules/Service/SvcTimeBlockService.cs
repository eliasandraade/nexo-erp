using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public record CreateSvcTimeBlockRequest(Guid? ProfessionalId, DateTime StartsAt, DateTime EndsAt, string? Reason = null);

public record SvcTimeBlockDto(Guid Id, Guid? ProfessionalId, DateTime StartsAt, DateTime EndsAt, string? Reason, DateTime CreatedAt);

/// <summary>
/// Bloqueios de agenda (folga, almoço, feriado). A block never cancels appointments already booked
/// in it — it stops new ones; the agenda shows both so the team decides what to move.
/// </summary>
public class SvcTimeBlockService
{
    private readonly ISvcTimeBlockRepository _blocks;
    private readonly ISvcProfessionalRepository _professionals;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public SvcTimeBlockService(
        ISvcTimeBlockRepository blocks, ISvcProfessionalRepository professionals,
        ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _blocks        = blocks;
        _professionals = professionals;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    public async Task<IReadOnlyList<SvcTimeBlockDto>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, Guid? professionalId, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc) throw new DomainException("O fim do período precisa ser depois do início.");
        if ((toUtc - fromUtc).TotalDays > 400) throw new DomainException("Período de até 400 dias.");
        return (await _blocks.GetRangeAsync(Utc(fromUtc), Utc(toUtc), professionalId, ct)).Select(Map).ToList();
    }

    public async Task<SvcTimeBlockDto> CreateAsync(CreateSvcTimeBlockRequest r, CancellationToken ct = default)
    {
        var professionalId = r.ProfessionalId is { } p && p != Guid.Empty ? p : (Guid?)null;   // empty = whole store
        if (professionalId is { } pid)
            _ = await _professionals.GetByIdAsync(pid, ct) ?? throw new NotFoundException("SvcProfessional", pid);
        var block = SvcTimeBlock.Create(_currentTenant.Id, professionalId, Utc(r.StartsAt), Utc(r.EndsAt), r.Reason, _currentUser.UserId);
        await _blocks.AddAsync(block, ct);
        await _blocks.SaveChangesAsync(ct);
        return Map(block);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var block = await _blocks.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcTimeBlock", id);
        _blocks.Remove(block);
        await _blocks.SaveChangesAsync(ct);
    }

    private static DateTime Utc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc   => v,
        DateTimeKind.Local => v.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };

    private static SvcTimeBlockDto Map(SvcTimeBlock b) => new(b.Id, b.ProfessionalId, b.StartsAt, b.EndsAt, b.Reason, b.CreatedAt);
}
