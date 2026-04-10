using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Cash;

public class CashService
{
    private readonly ICashRepository _cash;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public CashService(ICashRepository cash, ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _cash          = cash;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    public async Task<CashSessionDto?> GetOpenSessionAsync(CancellationToken ct = default)
    {
        var session = await _cash.GetOpenSessionAsync(ct);
        return session is null ? null : MapToDto(session, null);
    }

    public async Task<IReadOnlyList<CashSessionDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _cash.GetAllAsync(ct);
        return list.Select(s => MapToDto(s, null)).ToList();
    }

    public async Task<CashSessionDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var session = await _cash.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("CashSession", id);

        var movements = await _cash.GetMovementsBySessionAsync(id, ct);
        return MapToDto(session, movements);
    }

    public async Task<CashSessionDto> OpenAsync(OpenCashSessionRequest request, CancellationToken ct = default)
    {
        var existing = await _cash.GetOpenSessionByUserAsync(_currentUser.UserId, ct);
        if (existing is not null)
            throw new ConflictException("You already have an open cash session.");

        var session = CashSession.Open(
            _currentTenant.Id,
            _currentUser.UserId,
            request.OpeningBalance,
            request.Notes);

        await _cash.AddSessionAsync(session, ct);
        await _cash.SaveChangesAsync(ct);
        return MapToDto(session, null);
    }

    public async Task<CashSessionDto> CloseAsync(Guid sessionId, CloseCashSessionRequest request, CancellationToken ct = default)
    {
        var session = await _cash.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("CashSession", sessionId);

        session.Close(_currentUser.UserId, request.ClosingBalance);
        await _cash.SaveChangesAsync(ct);

        var movements = await _cash.GetMovementsBySessionAsync(sessionId, ct);
        return MapToDto(session, movements);
    }

    public async Task<CashMovementDto> AddMovementAsync(Guid sessionId, AddCashMovementRequest request, CancellationToken ct = default)
    {
        var session = await _cash.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("CashSession", sessionId);

        if (!session.IsOpen)
            throw new DomainException("Cash session is not open.");

        var movementType = Enum.Parse<CashMovementType>(request.MovementType, ignoreCase: true);

        var movement = CashMovement.Create(
            _currentTenant.Id,
            sessionId,
            movementType,
            request.Amount,
            request.Description,
            _currentUser.UserId,
            request.ReferenceType,
            request.ReferenceId);

        await _cash.AddMovementAsync(movement, ct);
        await _cash.SaveChangesAsync(ct);
        return MapMovementToDto(movement);
    }

    private static CashSessionDto MapToDto(CashSession s, IReadOnlyList<CashMovement>? movements) => new(
        s.Id,
        s.Status.ToString(),
        s.OpenedByUserId,
        s.OpenedBy?.FullName ?? string.Empty,
        s.ClosedByUserId,
        s.ClosedBy?.FullName,
        s.OpeningBalance,
        s.ClosingBalance,
        s.OpenedAt,
        s.ClosedAt,
        s.Notes,
        movements?.Select(MapMovementToDto).ToList());

    private static CashMovementDto MapMovementToDto(CashMovement m) => new(
        m.Id, m.MovementType.ToString(), m.Amount, m.Description,
        m.ReferenceType, m.ReferenceId, m.CreatedByUserId, m.CreatedAt);
}
